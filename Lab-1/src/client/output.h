#pragma once

#include <windows.h>

void PrintError(const wchar_t* what, long code);

void PrintProgress(ULONGLONG done, ULONGLONG total, ULONGLONG startTick);
