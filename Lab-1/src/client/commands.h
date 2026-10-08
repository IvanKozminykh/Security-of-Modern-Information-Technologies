#pragma once

void CmdLogin(const wchar_t* account, const wchar_t* password);

void CmdLogout(void);

void CmdWhoAmI(void);

void CmdUpload(const wchar_t* localPath, const wchar_t* uncPath);

void CmdDownload(const wchar_t* uncPath, const wchar_t* localPath);

void CmdDelete(const wchar_t* uncPath);
