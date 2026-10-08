#include "unc_path.h"

#include <wchar.h>

BOOL IsUncPath(const wchar_t* path)
{
    if (path == NULL || wcsncmp(path, L"\\\\", 2) != 0)
        return FALSE;
    if (path[2] == L'.' || path[2] == L'?')
        return FALSE;
    const wchar_t* sep = wcschr(path + 2, L'\\');
    if (sep == NULL || sep == path + 2 || sep[1] == L'\0')
        return FALSE;
    return wcslen(path) < FM_PATH_LEN;
}
