#pragma once

#include <windows.h>

#include "FileManager.h"

void ConnSetTarget(const wchar_t* host, const wchar_t* port, BOOL lrpc);

void ConnPrintTarget(void);

long ConnLogin(const wchar_t* account, const wchar_t* password);

long ConnLogout(void);

BOOL ConnIsLocal(void);

FM_SESSION ConnSession(void);

const wchar_t* ConnUser(void);
