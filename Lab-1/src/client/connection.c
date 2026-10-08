#include "connection.h"

#include <stdio.h>
#include <wchar.h>

#include "config.h"
#include "rpc_call.h"

#define CRED_LEN 128

static WCHAR g_host[256] = L"localhost";
static WCHAR g_port[16] = FM_DEFAULT_PORT;
static BOOL g_lrpc;

static RPC_BINDING_HANDLE g_binding;

static FM_SESSION g_session;

static WCHAR g_user[CRED_LEN];
static WCHAR g_domain[CRED_LEN];
static WCHAR g_password[CRED_LEN];

static SEC_WINNT_AUTH_IDENTITY_W g_identity;

void ConnSetTarget(const wchar_t* host, const wchar_t* port, BOOL lrpc)
{
    g_lrpc = lrpc;
    if (host != NULL)
        wcscpy_s(g_host, ARRAYSIZE(g_host), host);
    if (port != NULL)
        wcscpy_s(g_port, ARRAYSIZE(g_port), port);
}

void ConnPrintTarget(void)
{
    if (g_lrpc)
        wprintf(L"Сервер: локальный, ncalrpc:[%s]\n", FM_LRPC_ENDPOINT);
    else
        wprintf(L"Сервер: %s, ncacn_ip_tcp:[%s]\n", g_host, g_port);
}

BOOL ConnIsLocal(void)
{
    return g_lrpc;
}

FM_SESSION ConnSession(void)
{
    return g_session;
}

const wchar_t* ConnUser(void)
{
    return g_user;
}

static void FreeBinding(void)
{
    if (g_binding != NULL)
        RpcBindingFree(&g_binding);
    SecureZeroMemory(g_password, sizeof g_password);
    SecureZeroMemory(&g_identity, sizeof g_identity);
}

static RPC_STATUS CreateBinding(void)
{
    RPC_WSTR stringBinding = NULL;
    RPC_WSTR protseq;
    RPC_WSTR address;
    RPC_WSTR endpoint;
    RPC_STATUS status;

    if (g_lrpc) {
        protseq = (RPC_WSTR)L"ncalrpc";
        address = NULL;
        endpoint = (RPC_WSTR)FM_LRPC_ENDPOINT;
    } else {
        protseq = (RPC_WSTR)L"ncacn_ip_tcp";
        address = (RPC_WSTR)g_host;
        endpoint = (RPC_WSTR)g_port;
    }

    status = RpcStringBindingComposeW(NULL, protseq, address, endpoint, NULL, &stringBinding);
    if (status != RPC_S_OK)
        return status;
    status = RpcBindingFromStringBindingW(stringBinding, &g_binding);
    RpcStringFreeW(&stringBinding);
    return status;
}

static RPC_STATUS SetupSecurity(void)
{
    RPC_SECURITY_QOS qos;
    SEC_WINNT_AUTH_IDENTITY_W* identity;

    qos.Version = RPC_C_SECURITY_QOS_VERSION;
    qos.Capabilities = RPC_C_QOS_CAPABILITIES_DEFAULT;
    qos.IdentityTracking = RPC_C_QOS_IDENTITY_STATIC;
    qos.ImpersonationType = RPC_C_IMP_LEVEL_IDENTIFY;

    g_identity.User = (unsigned short*)g_user;
    g_identity.UserLength = (unsigned long)wcslen(g_user);
    g_identity.Domain = (unsigned short*)g_domain;
    g_identity.DomainLength = (unsigned long)wcslen(g_domain);
    g_identity.Password = (unsigned short*)g_password;
    g_identity.PasswordLength = (unsigned long)wcslen(g_password);
    g_identity.Flags = SEC_WINNT_AUTH_IDENTITY_UNICODE;

    if (g_lrpc)
        identity = NULL;
    else
        identity = &g_identity;

    return RpcBindingSetAuthInfoExW(g_binding, NULL, RPC_C_AUTHN_LEVEL_PKT_PRIVACY,
        RPC_C_AUTHN_WINNT, identity, RPC_C_AUTHZ_NONE, &qos);
}

long ConnLogin(const wchar_t* account, const wchar_t* password)
{
    const wchar_t* slash = wcschr(account, L'\\');
    RPC_STATUS status;
    long r;

    if (slash != NULL) {
        wcsncpy_s(g_domain, CRED_LEN, account, (size_t)(slash - account));
        wcscpy_s(g_user, CRED_LEN, slash + 1);
    } else {
        g_domain[0] = L'\0';
        wcscpy_s(g_user, CRED_LEN, account);
    }
    wcscpy_s(g_password, CRED_LEN, password);

    status = CreateBinding();
    if (status == RPC_S_OK)
        status = SetupSecurity();
    if (status != RPC_S_OK) {
        FreeBinding();
        return (long)status;
    }

    RPC_CALL(r, FmLogin(g_binding, g_domain, g_user, g_password, &g_session));
    if (r != 0) {
        g_session = NULL;
        FreeBinding();
    }
    return r;
}

long ConnLogout(void)
{
    long r = 0;

    if (g_session != NULL) {
        RPC_CALL(r, FmLogout(&g_session));
        if (r != 0)
            RpcSsDestroyClientContext(&g_session);
        g_session = NULL;
    }
    FreeBinding();
    return r;
}
