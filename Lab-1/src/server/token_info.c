#include "token_info.h"

#include <stdio.h>
#include <wchar.h>

#define ACCOUNT_LEN 256

static const wchar_t* IntegrityName(HANDLE token)
{
    union {
        TOKEN_MANDATORY_LABEL label;
        BYTE raw[128];
    } info;
    DWORD len;

    if (!GetTokenInformation(token, TokenIntegrityLevel, &info, sizeof info, &len))
        return L"?";
    PSID sid = info.label.Label.Sid;
    DWORD rid = *GetSidSubAuthority(sid, *GetSidSubAuthorityCount(sid) - 1);
    if (rid >= SECURITY_MANDATORY_SYSTEM_RID)
        return L"System";
    if (rid >= SECURITY_MANDATORY_HIGH_RID)
        return L"High";
    if (rid >= SECURITY_MANDATORY_MEDIUM_RID)
        return L"Medium";
    return L"Low";
}

static const wchar_t* ImpersonationLevelName(SECURITY_IMPERSONATION_LEVEL level)
{
    switch (level) {
    case SecurityAnonymous:
        return L"Anonymous";
    case SecurityIdentification:
        return L"Identification";
    case SecurityImpersonation:
        return L"Impersonation";
    case SecurityDelegation:
        return L"Delegation";
    default:
        return L"?";
    }
}

void TokenAccountName(HANDLE token, WCHAR* out, size_t cch)
{
    union {
        TOKEN_USER user;
        BYTE raw[512];
    } info;
    WCHAR name[128];
    WCHAR domain[128];
    DWORD cchName = ARRAYSIZE(name);
    DWORD cchDomain = ARRAYSIZE(domain);
    DWORD len;
    SID_NAME_USE use;

    wcscpy_s(out, cch, L"?");
    if (!GetTokenInformation(token, TokenUser, &info, sizeof info, &len))
        return;
    if (!LookupAccountSidW(NULL, info.user.User.Sid, name, &cchName, domain, &cchDomain, &use))
        return;
    swprintf_s(out, cch, L"%s\\%s", domain, name);
}

void DescribeToken(HANDLE token, WCHAR* out, size_t cch)
{
    WCHAR account[ACCOUNT_LEN];
    TOKEN_TYPE type;
    SECURITY_IMPERSONATION_LEVEL level;
    DWORD len;

    TokenAccountName(token, account, ARRAYSIZE(account));
    swprintf_s(out, cch, L"%s [IL: %s]", account, IntegrityName(token));

    if (!GetTokenInformation(token, TokenType, &type, sizeof type, &len))
        return;
    if (type != TokenImpersonation)
        return;
    if (!GetTokenInformation(token, TokenImpersonationLevel, &level, sizeof level, &len))
        return;
    swprintf_s(out, cch, L"%s [IL: %s, имперсонация: %s]",
        account, IntegrityName(token), ImpersonationLevelName(level));
}

void DescribeProcess(WCHAR* out, size_t cch)
{
    HANDLE token;

    if (OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) {
        DescribeToken(token, out, cch);
        CloseHandle(token);
    } else {
        wcscpy_s(out, cch, L"?");
    }
}

void DescribeThread(WCHAR* out, size_t cch)
{
    HANDLE token;

    if (OpenThreadToken(GetCurrentThread(), TOKEN_QUERY, TRUE, &token) ||
        OpenThreadToken(GetCurrentThread(), TOKEN_QUERY, FALSE, &token)) {
        DescribeToken(token, out, cch);
        CloseHandle(token);
    } else if (GetLastError() == ERROR_NO_TOKEN) {
        wcscpy_s(out, cch, L"(нет имперсонации - права процесса)");
    } else {
        wcscpy_s(out, cch, L"?");
    }
}
