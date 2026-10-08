#include <windows.h>
#include <wchar.h>

#include "FileManager.h"
#include "log.h"
#include "session.h"
#include "token_info.h"
#include "unc_path.h"

static DWORD OpenTransferFile(SESSION* s, const wchar_t* op, const wchar_t* path,
    FILE_MODE mode, DWORD access, DWORD share, DWORD disposition)
{
    WCHAR thread[FM_NAME_LEN];
    WCHAR result[320];
    DWORD flags = FILE_ATTRIBUTE_NORMAL | FILE_FLAG_SEQUENTIAL_SCAN;
    DWORD err;

    if (!IsUncPath(path)) {
        if (path == NULL)
            path = L"(null)";
        Log(L"%s | %s | %s %s -> путь не в формате UNC", s->client, s->user, op, path);
        return ERROR_BAD_PATHNAME;
    }
    SessionCloseFile(s);

    err = SessionImpersonate(s);
    if (err != ERROR_SUCCESS) {
        Log(L"%s | %s | ImpersonateLoggedOnUser: %s",
            s->client, s->user, ResultText(err, result, ARRAYSIZE(result)));
        return err;
    }
    DescribeThread(thread, ARRAYSIZE(thread));

    s->file = CreateFileW(path, access, share, NULL, disposition, flags, NULL);
    if (s->file == INVALID_HANDLE_VALUE)
        err = GetLastError();
    else
        err = ERROR_SUCCESS;

    SessionRevert();

    Log(L"%s | %s | %s %s\n             поток: %s -> %s",
        s->client, s->user, op, path, thread, ResultText(err, result, ARRAYSIZE(result)));
    if (err == ERROR_SUCCESS) {
        s->mode = mode;
        wcscpy_s(s->path, ARRAYSIZE(s->path), path);
    }
    return err;
}

long FmBeginUpload(FM_SESSION session, const wchar_t* uncPath)
{
    SESSION* s = SessionFromHandle(session);
    DWORD err;

    if (s == NULL)
        return ERROR_INVALID_HANDLE;

    err = OpenTransferFile(s, L"UPLOAD", uncPath, MODE_UPLOAD,
        GENERIC_WRITE, 0, CREATE_ALWAYS);
    return (long)err;
}

long FmWriteChunk(FM_SESSION session, unsigned long size, const byte* data)
{
    SESSION* s = SessionFromHandle(session);
    DWORD written = 0;
    DWORD err;

    if (s == NULL)
        return ERROR_INVALID_HANDLE;
    if (s->mode != MODE_UPLOAD)
        return ERROR_INVALID_FUNCTION;

    err = SessionImpersonate(s);
    if (err != ERROR_SUCCESS)
        return (long)err;
    if (WriteFile(s->file, data, size, &written, NULL))
        err = ERROR_SUCCESS;
    else
        err = GetLastError();
    SessionRevert();

    if (err == ERROR_SUCCESS)
        s->transferred += written;
    return (long)err;
}

long FmBeginDownload(FM_SESSION session, const wchar_t* uncPath, hyper* fileSize)
{
    SESSION* s = SessionFromHandle(session);
    LARGE_INTEGER size;
    DWORD err;

    *fileSize = 0;
    if (s == NULL)
        return ERROR_INVALID_HANDLE;

    err = OpenTransferFile(s, L"DOWNLOAD", uncPath, MODE_DOWNLOAD,
        GENERIC_READ, FILE_SHARE_READ, OPEN_EXISTING);
    if (err != ERROR_SUCCESS)
        return (long)err;

    if (!GetFileSizeEx(s->file, &size)) {
        err = GetLastError();
        SessionCloseFile(s);
        return (long)err;
    }
    *fileSize = size.QuadPart;
    return ERROR_SUCCESS;
}

long FmReadChunk(FM_SESSION session, unsigned long maxSize, byte* data, unsigned long* bytesRead)
{
    SESSION* s = SessionFromHandle(session);
    DWORD read = 0;
    DWORD err;

    *bytesRead = 0;
    if (s == NULL)
        return ERROR_INVALID_HANDLE;
    if (s->mode != MODE_DOWNLOAD)
        return ERROR_INVALID_FUNCTION;

    err = SessionImpersonate(s);
    if (err != ERROR_SUCCESS)
        return (long)err;
    if (ReadFile(s->file, data, maxSize, &read, NULL))
        err = ERROR_SUCCESS;
    else
        err = GetLastError();
    SessionRevert();

    if (err == ERROR_SUCCESS) {
        s->transferred += read;
        *bytesRead = read;
    }
    return (long)err;
}

long FmCloseFile(FM_SESSION session)
{
    SESSION* s = SessionFromHandle(session);
    const wchar_t* op;

    if (s == NULL)
        return ERROR_INVALID_HANDLE;

    if (s->mode != MODE_NONE) {
        if (s->mode == MODE_UPLOAD)
            op = L"UPLOAD";
        else
            op = L"DOWNLOAD";
        Log(L"%s | %s | %s %s: передано %llu байт",
            s->client, s->user, op, s->path, s->transferred);
    }
    SessionCloseFile(s);
    return ERROR_SUCCESS;
}

long FmDeleteFile(FM_SESSION session, const wchar_t* uncPath)
{
    SESSION* s = SessionFromHandle(session);
    WCHAR thread[FM_NAME_LEN];
    WCHAR result[320];
    DWORD err;

    if (s == NULL)
        return ERROR_INVALID_HANDLE;
    if (!IsUncPath(uncPath)) {
        Log(L"%s | %s | DELETE %s -> путь не в формате UNC", s->client, s->user, uncPath);
        return ERROR_BAD_PATHNAME;
    }

    err = SessionImpersonate(s);
    if (err != ERROR_SUCCESS)
        return (long)err;
    DescribeThread(thread, ARRAYSIZE(thread));
    if (DeleteFileW(uncPath))
        err = ERROR_SUCCESS;
    else
        err = GetLastError();
    SessionRevert();

    Log(L"%s | %s | DELETE %s\n             поток: %s -> %s",
        s->client, s->user, uncPath, thread, ResultText(err, result, ARRAYSIZE(result)));
    return (long)err;
}
