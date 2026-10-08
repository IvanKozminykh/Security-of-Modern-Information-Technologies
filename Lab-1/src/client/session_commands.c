#include <windows.h>
#include <stdio.h>

#include "commands.h"
#include "connection.h"
#include "input.h"
#include "output.h"
#include "rpc_call.h"

void CmdWhoAmI(void)
{
    WCHAR processUser[FM_NAME_LEN];
    WCHAR threadUser[FM_NAME_LEN];
    long r;

    if (ConnSession() == NULL) {
        wprintf(L"  Сначала выполните login.\n");
        return;
    }
    RPC_CALL(r, FmWhoAmI(ConnSession(), processUser, threadUser));
    if (r != 0) {
        PrintError(L"whoami", r);
        return;
    }
    processUser[FM_NAME_LEN - 1] = L'\0';
    threadUser[FM_NAME_LEN - 1] = L'\0';
    wprintf(L"  Процесс сервера:              %s\n", processUser);
    wprintf(L"  Поток сервера (имперсонация): %s\n", threadUser);
}

void CmdLogin(const wchar_t* account, const wchar_t* password)
{
    WCHAR typed[128];
    long r;

    if (ConnSession() != NULL) {
        wprintf(L"  Уже выполнен вход как %s. Сначала logout.\n", ConnUser());
        return;
    }
    if (password == NULL) {
        ReadPassword(typed, ARRAYSIZE(typed));
        password = typed;
    }

    r = ConnLogin(account, password);
    SecureZeroMemory(typed, sizeof typed);
    if (r != 0) {
        PrintError(L"login", r);
        if (r == RPC_S_ACCESS_DENIED && !ConnIsLocal())
            wprintf(L"  (учётные данные отклонены при аутентификации RPC-соединения)\n");
        return;
    }
    wprintf(L"  Вход выполнен: %s\n", account);
    CmdWhoAmI();
}

void CmdLogout(void)
{
    long r;

    if (ConnSession() == NULL)
        return;
    r = ConnLogout();
    if (r != 0)
        PrintError(L"logout", r);
    else
        wprintf(L"  Сеанс завершён.\n");
}
