#pragma once

#include <windows.h>

#include "FileManager.h"
#include "unc_path.h"

typedef enum {
    MODE_NONE,
    MODE_UPLOAD,
    MODE_DOWNLOAD
} FILE_MODE;

typedef struct SESSION {
    DWORD magic;
    HANDLE token;
    WCHAR user[FM_NAME_LEN];
    WCHAR client[64];
    HANDLE file;
    FILE_MODE mode;
    WCHAR path[FM_PATH_LEN];
    ULONGLONG transferred;
} SESSION;

SESSION* SessionCreate(HANDLE token, const wchar_t* client);

void SessionDestroy(SESSION* s);

SESSION* SessionFromHandle(FM_SESSION handle);

LONG SessionCount(void);

DWORD SessionImpersonate(SESSION* s);

void SessionRevert(void);

void SessionCloseFile(SESSION* s);
