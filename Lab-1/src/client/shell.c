#include "shell.h"

#include <windows.h>
#include <stdio.h>
#include <wchar.h>

#include "commands.h"
#include "connection.h"
#include "input.h"

#define MAX_ARGS 4

typedef struct {
    const wchar_t* name;
    int minArgs;
    int maxArgs;
    void (*run)(wchar_t** args, int count);
} COMMAND;

static void RunLogin(wchar_t** args, int count)
{
    if (count > 1)
        CmdLogin(args[0], args[1]);
    else
        CmdLogin(args[0], NULL);
}

static void RunWhoAmI(wchar_t** args, int count)
{
    UNREFERENCED_PARAMETER(args);
    UNREFERENCED_PARAMETER(count);
    CmdWhoAmI();
}

static void RunUpload(wchar_t** args, int count)
{
    UNREFERENCED_PARAMETER(count);
    CmdUpload(args[0], args[1]);
}

static void RunDownload(wchar_t** args, int count)
{
    UNREFERENCED_PARAMETER(count);
    CmdDownload(args[0], args[1]);
}

static void RunDelete(wchar_t** args, int count)
{
    UNREFERENCED_PARAMETER(count);
    CmdDelete(args[0]);
}

static void RunLogout(wchar_t** args, int count)
{
    UNREFERENCED_PARAMETER(args);
    UNREFERENCED_PARAMETER(count);
    CmdLogout();
}

static void RunHelp(wchar_t** args, int count)
{
    UNREFERENCED_PARAMETER(args);
    UNREFERENCED_PARAMETER(count);
    ShellPrintHelp();
}

static const COMMAND kCommands[] = {
    { L"login", 1, 2, RunLogin },
    { L"whoami", 0, 0, RunWhoAmI },
    { L"upload", 2, 2, RunUpload },
    { L"download", 2, 2, RunDownload },
    { L"delete", 1, 1, RunDelete },
    { L"logout", 0, 0, RunLogout },
    { L"help", 0, 0, RunHelp },
};

void ShellPrintHelp(void)
{
    wprintf(L"Команды:\n");
    wprintf(L"  login <пользователь|домен\\пользователь> [пароль]\n");
    wprintf(L"  whoami - от чьего имени работает сервер\n");
    wprintf(L"  upload <локальный файл> <UNC> - копирование файла на сервер\n");
    wprintf(L"  download <UNC> <локальный файл> - загрузка файла с сервера\n");
    wprintf(L"  delete <UNC> - удаление файла на сервере\n");
    wprintf(L"  logout\n");
    wprintf(L"  help\n");
    wprintf(L"  exit\n");
    wprintf(L"Пути с пробелами заключайте в кавычки. Пример UNC: \\\\server\\share\\file.exe\n");
}

static BOOL Execute(wchar_t** argv, int argc)
{
    const wchar_t* name = argv[0];

    if (_wcsicmp(name, L"exit") == 0 || _wcsicmp(name, L"quit") == 0)
        return FALSE;

    for (size_t i = 0; i < ARRAYSIZE(kCommands); i++) {
        const COMMAND* c = &kCommands[i];
        if (_wcsicmp(name, c->name) != 0)
            continue;
        if (argc - 1 < c->minArgs || argc - 1 > c->maxArgs) {
            wprintf(L"  Неверное число аргументов команды %s.\n", c->name);
            ShellPrintHelp();
        } else {
            c->run(argv + 1, argc - 1);
        }
        return TRUE;
    }
    wprintf(L"  Неизвестная команда: %s\n", name);
    ShellPrintHelp();
    return TRUE;
}

void ShellRun(void)
{
    wchar_t line[2048];
    wchar_t* argv[MAX_ARGS];

    for (;;) {
        if (ConnSession() != NULL)
            wprintf(L"\nfm:%s> ", ConnUser());
        else
            wprintf(L"\nfm> ");
        fflush(stdout);

        if (!ReadLine(line, ARRAYSIZE(line)))
            break;

        int argc = Tokenize(line, argv, MAX_ARGS);
        BOOL proceed = TRUE;
        if (argc > 0)
            proceed = Execute(argv, argc);

        SecureZeroMemory(line, sizeof line);
        if (!proceed)
            break;
    }
}
