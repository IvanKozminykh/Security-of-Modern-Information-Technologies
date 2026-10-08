#include <windows.h>

#include "FileManager.h"
#include "config.h"
#include "console.h"
#include "log.h"
#include "rpc_server.h"
#include "token_info.h"

static BOOL WINAPI CtrlHandler(DWORD type)
{
    UNREFERENCED_PARAMETER(type);
    Log(L"Остановка сервера...");

    ServerRequestStop();
    return TRUE;
}

int wmain(int argc, wchar_t** argv)
{
    const wchar_t* port = FM_DEFAULT_PORT;
    WCHAR who[FM_NAME_LEN];

    if (argc > 1)
        port = argv[1];

    ConsoleInit();
    LogInit();

    DescribeProcess(who, ARRAYSIZE(who));
    Log(L"Сервер FileManager запущен, процесс работает от имени %s", who);

    if (ServerStart(port) != RPC_S_OK)
        return 1;

    SetConsoleCtrlHandler(CtrlHandler, TRUE);

    ServerRun();

    ServerCleanup();

    Log(L"Сервер остановлен");
    return 0;
}
