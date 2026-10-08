#include "log.h"

#include <stdarg.h>
#include <stdio.h>

#include "errors.h"

static CRITICAL_SECTION g_logLock;

void LogInit(void)
{
    InitializeCriticalSection(&g_logLock);
}

void Log(const wchar_t* fmt, ...)
{
    SYSTEMTIME t;
    va_list ap;

    GetLocalTime(&t);
    va_start(ap, fmt);
    EnterCriticalSection(&g_logLock);
    wprintf(L"[%02u:%02u:%02u.%03u] ", t.wHour, t.wMinute, t.wSecond, t.wMilliseconds);
    vwprintf(fmt, ap);
    wprintf(L"\n");
    fflush(stdout);
    LeaveCriticalSection(&g_logLock);
    va_end(ap);
}

const wchar_t* ResultText(DWORD code, wchar_t* buf, DWORD cch)
{
    wchar_t msg[256];

    if (code == ERROR_SUCCESS)
        return L"OK";
    swprintf_s(buf, cch, L"ОШИБКА %lu (%s)", code, ErrorText(code, msg, ARRAYSIZE(msg)));
    return buf;
}
