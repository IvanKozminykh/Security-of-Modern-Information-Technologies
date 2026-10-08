#pragma once

#include <windows.h>

RPC_STATUS ServerStart(const wchar_t* port);

RPC_STATUS ServerRun(void);

void ServerRequestStop(void);

void ServerCleanup(void);
