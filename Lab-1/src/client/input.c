#include "input.h"

#include <windows.h>
#include <stdio.h>
#include <wchar.h>
#include <wctype.h>

int ReadLine(wchar_t* buf, size_t cch)
{
    if (fgetws(buf, (int)cch, stdin) == NULL)
        return 0;
    buf[wcscspn(buf, L"\r\n")] = L'\0';
    if (buf[0] == 0xFEFF)
        buf[0] = L' ';
    return 1;
}

void ReadPassword(wchar_t* buf, size_t cch)
{
    HANDLE in = GetStdHandle(STD_INPUT_HANDLE);
    DWORD mode;
    BOOL console = GetConsoleMode(in, &mode);

    wprintf(L"Пароль: ");
    fflush(stdout);
    if (console)
        SetConsoleMode(in, mode & ~ENABLE_ECHO_INPUT);
    if (!ReadLine(buf, cch))
        buf[0] = L'\0';
    if (console) {
        SetConsoleMode(in, mode);
        wprintf(L"\n");
    }
}

int Tokenize(wchar_t* line, wchar_t** argv, int maxArgs)
{
    int argc = 0;
    wchar_t* p = line;

    while (*p != L'\0' && argc < maxArgs) {
        while (iswspace(*p))
            p++;
        if (*p == L'\0')
            break;
        if (*p == L'"') {
            p++;
            argv[argc] = p;
            argc++;
            while (*p != L'\0' && *p != L'"')
                p++;
        } else {
            argv[argc] = p;
            argc++;
            while (*p != L'\0' && !iswspace(*p))
                p++;
        }
        if (*p != L'\0') {
            *p = L'\0';
            p++;
        }
    }
    return argc;
}
