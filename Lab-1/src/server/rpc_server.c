#include "rpc_server.h"

#include "FileManager.h"
#include "config.h"
#include "errors.h"
#include "log.h"

#define MAX_RPC_SIZE (FM_MAX_CHUNK + 64 * 1024)

static RPC_BINDING_VECTOR* g_bindings;

static RPC_STATUS Report(const wchar_t* what, RPC_STATUS status)
{
    WCHAR msg[256];

    if (status != RPC_S_OK)
        Log(L"%s: ошибка %ld (%s)", what, status, ErrorText((DWORD)status, msg, ARRAYSIZE(msg)));
    return status;
}

static RPC_STATUS CALLBACK SecurityCallback(RPC_IF_HANDLE ifSpec, void* context)
{
    unsigned long level = 0;
    unsigned long service = 0;
    UNREFERENCED_PARAMETER(ifSpec);

    if (RpcBindingInqAuthClientW(context, NULL, NULL, &level, &service, NULL) != RPC_S_OK)
        return ERROR_ACCESS_DENIED;
    if (level < RPC_C_AUTHN_LEVEL_PKT_PRIVACY)
        return ERROR_ACCESS_DENIED;
    return RPC_S_OK;
}

RPC_STATUS ServerStart(const wchar_t* port)
{
    unsigned int maxReqs = RPC_C_PROTSEQ_MAX_REQS_DEFAULT;
    unsigned int maxCalls = RPC_C_LISTEN_MAX_CALLS_DEFAULT;
    RPC_IF_HANDLE ifSpec = FileManager_v1_0_s_ifspec;
    RPC_STATUS status;

    status = RpcServerUseProtseqEpW((RPC_WSTR)L"ncacn_ip_tcp", maxReqs, (RPC_WSTR)port, NULL);
    if (Report(L"RpcServerUseProtseqEp(ncacn_ip_tcp)", status) != RPC_S_OK)
        return status;

    status = RpcServerUseProtseqEpW((RPC_WSTR)L"ncalrpc", maxReqs, (RPC_WSTR)FM_LRPC_ENDPOINT, NULL);
    if (Report(L"RpcServerUseProtseqEp(ncalrpc)", status) != RPC_S_OK)
        return status;

    status = RpcServerRegisterAuthInfoW(NULL, RPC_C_AUTHN_WINNT, NULL, NULL);
    if (Report(L"RpcServerRegisterAuthInfo", status) != RPC_S_OK)
        return status;

    status = RpcServerRegisterIf2(ifSpec, NULL, NULL, RPC_IF_ALLOW_SECURE_ONLY,
        maxCalls, MAX_RPC_SIZE, SecurityCallback);
    if (Report(L"RpcServerRegisterIf2", status) != RPC_S_OK)
        return status;

    status = RpcServerInqBindings(&g_bindings);
    if (Report(L"RpcServerInqBindings", status) != RPC_S_OK)
        return status;

    status = RpcEpRegisterW(ifSpec, g_bindings, NULL, (RPC_WSTR)L"Lab-1 FileManager");
    Report(L"RpcEpRegister (не критично, порт задан явно)", status);

    Log(L"Ожидание клиентов: ncacn_ip_tcp порт %s, ncalrpc %s. Ctrl+C - остановка.",
        port, FM_LRPC_ENDPOINT);
    return RPC_S_OK;
}

RPC_STATUS ServerRun(void)
{
    RPC_STATUS status;

    status = RpcServerListen(1, RPC_C_LISTEN_MAX_CALLS_DEFAULT, FALSE);
    return Report(L"RpcServerListen", status);
}

void ServerRequestStop(void)
{
    RpcMgmtStopServerListening(NULL);
}

void ServerCleanup(void)
{
    if (g_bindings != NULL) {
        RpcEpUnregister(FileManager_v1_0_s_ifspec, g_bindings, NULL);
        RpcBindingVectorFree(&g_bindings);
    }
    RpcServerUnregisterIf(NULL, NULL, FALSE);
}
