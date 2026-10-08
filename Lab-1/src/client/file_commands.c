#include <windows.h>
#include <stdio.h>
#include <stdlib.h>

#include "commands.h"
#include "connection.h"
#include "output.h"
#include "rpc_call.h"

static BOOL RequireSession(void)
{
    if (ConnSession() == NULL) {
        wprintf(L"  Сначала выполните login.\n");
        return FALSE;
    }
    return TRUE;
}

static long SendFile(HANDLE file, ULONGLONG total, ULONGLONG* done)
{
    BYTE* buffer;
    ULONGLONG start;
    DWORD n;
    long r = 0;

    buffer = malloc(FM_MAX_CHUNK);
    if (buffer == NULL)
        return ERROR_NOT_ENOUGH_MEMORY;

    start = GetTickCount64();
    while (TRUE) {
        if (!ReadFile(file, buffer, FM_MAX_CHUNK, &n, NULL)) {
            r = (long)GetLastError();
            break;
        }
        if (n == 0)
            break;

        RPC_CALL(r, FmWriteChunk(ConnSession(), n, buffer));
        if (r != 0)
            break;

        *done += n;
        PrintProgress(*done, total, start);
    }
    wprintf(L"\n");

    free(buffer);
    return r;
}

static long ReceiveFile(HANDLE file, ULONGLONG total, ULONGLONG* done)
{
    BYTE* buffer;
    ULONGLONG start;
    unsigned long n;
    DWORD written;
    long r = 0;

    buffer = malloc(FM_MAX_CHUNK);
    if (buffer == NULL)
        return ERROR_NOT_ENOUGH_MEMORY;

    start = GetTickCount64();
    while (TRUE) {
        n = 0;
        RPC_CALL(r, FmReadChunk(ConnSession(), FM_MAX_CHUNK, buffer, &n));
        if (r != 0)
            break;
        if (n == 0)
            break;

        if (!WriteFile(file, buffer, n, &written, NULL)) {
            r = (long)GetLastError();
            break;
        }

        *done += n;
        PrintProgress(*done, total, start);
    }
    wprintf(L"\n");

    free(buffer);
    return r;
}

void CmdUpload(const wchar_t* localPath, const wchar_t* uncPath)
{
    LARGE_INTEGER size;
    ULONGLONG done = 0;
    HANDLE file;
    long r;
    long rc;

    if (!RequireSession())
        return;

    file = CreateFileW(localPath, GENERIC_READ, FILE_SHARE_READ, NULL,
        OPEN_EXISTING, FILE_FLAG_SEQUENTIAL_SCAN, NULL);
    if (file == INVALID_HANDLE_VALUE) {
        PrintError(L"открытие локального файла", (long)GetLastError());
        return;
    }
    GetFileSizeEx(file, &size);

    RPC_CALL(r, FmBeginUpload(ConnSession(), uncPath));
    if (r != 0) {
        PrintError(L"upload", r);
        CloseHandle(file);
        return;
    }

    r = SendFile(file, (ULONGLONG)size.QuadPart, &done);
    CloseHandle(file);

    RPC_CALL(rc, FmCloseFile(ConnSession()));
    if (r != 0)
        PrintError(L"upload", r);
    else if (rc != 0)
        PrintError(L"upload (закрытие файла)", rc);
    else
        wprintf(L"  Загружено на сервер: %llu байт -> %s\n", done, uncPath);
}

void CmdDownload(const wchar_t* uncPath, const wchar_t* localPath)
{
    hyper size = 0;
    ULONGLONG done = 0;
    HANDLE file;
    long r;
    long rc;

    if (!RequireSession())
        return;

    RPC_CALL(r, FmBeginDownload(ConnSession(), uncPath, &size));
    if (r != 0) {
        PrintError(L"download", r);
        return;
    }

    file = CreateFileW(localPath, GENERIC_WRITE, 0, NULL,
        CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
    if (file == INVALID_HANDLE_VALUE) {
        PrintError(L"создание локального файла", (long)GetLastError());
        RPC_CALL(rc, FmCloseFile(ConnSession()));
        return;
    }

    r = ReceiveFile(file, (ULONGLONG)size, &done);
    CloseHandle(file);
    RPC_CALL(rc, FmCloseFile(ConnSession()));

    if (r == 0 && done != (ULONGLONG)size)
        r = ERROR_HANDLE_EOF;

    if (r != 0) {
        PrintError(L"download", r);
        DeleteFileW(localPath);
    } else {
        wprintf(L"  Скачано с сервера: %llu байт -> %s\n", done, localPath);
    }
}

void CmdDelete(const wchar_t* uncPath)
{
    long r;

    if (!RequireSession())
        return;

    RPC_CALL(r, FmDeleteFile(ConnSession(), uncPath));
    if (r != 0)
        PrintError(L"delete", r);
    else
        wprintf(L"  Файл удалён: %s\n", uncPath);
}
