#include "session.h"

#include <stdlib.h>
#include <wchar.h>

#include "log.h"
#include "token_info.h"

#define SESSION_MAGIC 0x53534D46u

static volatile LONG g_sessions;

SESSION* SessionCreate(HANDLE token, const wchar_t* client)
{
    SESSION* s = calloc(1, sizeof *s);
    if (s == NULL)
        return NULL;

    s->magic = SESSION_MAGIC;
    s->token = token;
    s->file = INVALID_HANDLE_VALUE;
    s->mode = MODE_NONE;
    TokenAccountName(token, s->user, ARRAYSIZE(s->user));
    wcscpy_s(s->client, ARRAYSIZE(s->client), client);
    InterlockedIncrement(&g_sessions);
    return s;
}

void SessionDestroy(SESSION* s)
{
    SessionCloseFile(s);
    CloseHandle(s->token);
    SecureZeroMemory(s, sizeof *s);
    free(s);
    InterlockedDecrement(&g_sessions);
}

SESSION* SessionFromHandle(FM_SESSION handle)
{
    SESSION* s = (SESSION*)handle;

    if (s == NULL)
        return NULL;
    if (s->magic != SESSION_MAGIC)
        return NULL;
    return s;
}

LONG SessionCount(void)
{
    return g_sessions;
}

DWORD SessionImpersonate(SESSION* s)
{
    if (!ImpersonateLoggedOnUser(s->token))
        return GetLastError();
    return ERROR_SUCCESS;
}

void SessionRevert(void)
{
    if (!RevertToSelf()) {
        Log(L"FATAL: RevertToSelf завершился с ошибкой %lu", GetLastError());
        ExitProcess(1);
    }
}

void SessionCloseFile(SESSION* s)
{
    if (s->file != INVALID_HANDLE_VALUE) {
        CloseHandle(s->file);
        s->file = INVALID_HANDLE_VALUE;
    }
    s->mode = MODE_NONE;
    s->path[0] = L'\0';
    s->transferred = 0;
}
