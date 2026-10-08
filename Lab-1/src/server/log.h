#pragma once

#include <windows.h>

void LogInit(void);

void Log(const wchar_t* fmt, ...);

const wchar_t* ResultText(DWORD code, wchar_t* buf, DWORD cch);
