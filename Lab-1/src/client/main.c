#include <windows.h>
#include <stdio.h>
#include <wchar.h>

#include "commands.h"
#include "connection.h"
#include "console.h"
#include "shell.h"

int wmain(int argc, wchar_t** argv)
{
    ConsoleInit();

    if (argc < 2 || wcscmp(argv[1], L"-h") == 0 || wcscmp(argv[1], L"/?") == 0) {
        wprintf(L"Использование: fm_client.exe <сервер> [порт]\n");
        wprintf(L"               fm_client.exe -lrpc\n");
        return 1;
    }

    if (_wcsicmp(argv[1], L"-lrpc") == 0)
        ConnSetTarget(NULL, NULL, TRUE);
    else if (argc > 2)
        ConnSetTarget(argv[1], argv[2], FALSE);
    else
        ConnSetTarget(argv[1], NULL, FALSE);

    ConnPrintTarget();
    ShellPrintHelp();

    ShellRun();

    CmdLogout();
    return 0;
}
