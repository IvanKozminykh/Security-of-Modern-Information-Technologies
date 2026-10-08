#include <windows.h>
#include <wchar.h>

#include "FileManager.h"
#include "log.h"
#include "session.h"
#include "token_info.h"

static void ClientAddress(handle_t binding, WCHAR* out, size_t cch)
{
    RPC_BINDING_HANDLE serverBinding;
    RPC_WSTR str = NULL;
    RPC_WSTR addr = NULL;

    wcscpy_s(out, cch, L"local");
    if (RpcBindingServerFromClient(binding, &serverBinding) != RPC_S_OK)
        return;

    if (RpcBindingToStringBindingW(serverBinding, &str) == RPC_S_OK) {
        if (RpcStringBindingParseW(str, NULL, NULL, &addr, NULL, NULL) == RPC_S_OK) {
            if (addr != NULL && addr[0] != L'\0')
                wcscpy_s(out, cch, (const wchar_t*)addr);
            RpcStringFreeW(&addr);
        }
        RpcStringFreeW(&str);
    }
    RpcBindingFree(&serverBinding);
}

long FmLogin(handle_t hBinding, const wchar_t* domain, const wchar_t* user,
    const wchar_t* password, FM_SESSION* session)
{
    WCHAR client[64];
    WCHAR result[320];
    WCHAR desc[FM_NAME_LEN];
    const wchar_t* logonDomain = L".";
    HANDLE token;
    DWORD err = ERROR_SUCCESS;
    SESSION* s;

    *session = NULL;
    ClientAddress(hBinding, client, ARRAYSIZE(client));

    if (domain != NULL && domain[0] != L'\0')
        logonDomain = domain;

    if (!LogonUserW(user, logonDomain, password,
            LOGON32_LOGON_INTERACTIVE, LOGON32_PROVIDER_DEFAULT, &token))
        err = GetLastError();
    SecureZeroMemory((void*)password, wcslen(password) * sizeof(wchar_t));

    if (err != ERROR_SUCCESS) {
        Log(L"%s | LOGIN %s\\%s -> %s",
            client, logonDomain, user, ResultText(err, result, ARRAYSIZE(result)));
        return (long)err;
    }

    s = SessionCreate(token, client);
    if (s == NULL) {
        CloseHandle(token);
        return ERROR_NOT_ENOUGH_MEMORY;
    }

    DescribeToken(token, desc, ARRAYSIZE(desc));
    Log(L"%s | LOGIN %s -> OK, токен: %s, активных сессий: %ld",
        client, s->user, desc, SessionCount());
    *session = s;
    return ERROR_SUCCESS;
}

long FmLogout(FM_SESSION* session)
{
    SESSION* s = SessionFromHandle(*session);

    if (s == NULL)
        return ERROR_INVALID_HANDLE;

    Log(L"%s | %s | LOGOUT", s->client, s->user);
    SessionDestroy(s);
    *session = NULL;
    return ERROR_SUCCESS;
}

long FmWhoAmI(FM_SESSION session, wchar_t processUser[FM_NAME_LEN],
    wchar_t threadUser[FM_NAME_LEN])
{
    SESSION* s = SessionFromHandle(session);
    DWORD err;

    processUser[0] = L'\0';
    threadUser[0] = L'\0';
    if (s == NULL)
        return ERROR_INVALID_HANDLE;

    DescribeProcess(processUser, FM_NAME_LEN);

    err = SessionImpersonate(s);
    if (err != ERROR_SUCCESS)
        return (long)err;
    DescribeThread(threadUser, FM_NAME_LEN);
    SessionRevert();

    Log(L"%s | %s | WHOAMI\n             процесс: %s\n             поток:   %s",
        s->client, s->user, processUser, threadUser);
    return ERROR_SUCCESS;
}

void __RPC_USER FM_SESSION_rundown(FM_SESSION session)
{
    SESSION* s = SessionFromHandle(session);

    if (s == NULL)
        return;

    Log(L"%s | %s | соединение разорвано без LOGOUT, сессия закрыта", s->client, s->user);
    SessionDestroy(s);
}
