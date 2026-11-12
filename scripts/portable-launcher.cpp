// Native Windows Portable entry point (built per architecture by package.ps1).
// Keeps the self-contained WPF/.NET runtime and its DLLs in App/ rather than
// scattering 100+ files beside the launcher. The public EXE carries the same
// icon as the application and forwards process arguments and exit code.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <string>
#include <cwchar>

static bool IsFile(const std::wstring& file) {
    DWORD flags = GetFileAttributesW(file.c_str());
    return flags != INVALID_FILE_ATTRIBUTES && !(flags & FILE_ATTRIBUTE_DIRECTORY);
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR arguments, int) {
    wchar_t self[32768] = {};
    const DWORD count = GetModuleFileNameW(nullptr, self, 32768);
    if (!count || count >= 32768) {
        MessageBoxW(nullptr, L"Cannot determine launcher folder.", L"Najm Card New",
            MB_OK | MB_ICONERROR);
        return 2;
    }

    std::wstring root(self, count);
    const size_t last = root.find_last_of(L"\\/");
    if (last == std::wstring::npos) return 2;
    root.resize(last);
    const std::wstring appFolder = root + L"\\App";
    const std::wstring application = appFolder + L"\\NajmCard.exe";

    // Packaging smoke check: never starts application, never requires RouterOS.
    if (arguments && wcscmp(arguments, L"--verify-layout") == 0)
        return IsFile(application) ? 0 : 3;

    if (!IsFile(application)) {
        MessageBoxW(nullptr,
            L"App\\NajmCard.exe is missing. Extract the complete Portable ZIP; "
            L"do not copy the launcher alone.",
            L"Najm Card New", MB_OK | MB_ICONERROR);
        return 3;
    }

    std::wstring command = L"\"" + application + L"\"";
    if (arguments && *arguments) {
        command += L" ";
        command += arguments;
    }
    STARTUPINFOW startup = {};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION child = {};
    // The real WPF app runs from its own folder, preserving native DLL
    // probing, side-by-side dependencies, local resource paths and .NET 10.
    if (!CreateProcessW(application.c_str(), command.data(),
        nullptr, nullptr, FALSE, 0, nullptr, appFolder.c_str(),
        &startup, &child)) {
        const DWORD error = GetLastError();
        std::wstring msg = L"Cannot start App\\NajmCard.exe. Windows error: "
            + std::to_wstring(error);
        MessageBoxW(nullptr, msg.c_str(), L"Najm Card New", MB_OK | MB_ICONERROR);
        return 4;
    }
    CloseHandle(child.hThread);
    WaitForSingleObject(child.hProcess, INFINITE);
    DWORD exitCode = 1;
    GetExitCodeProcess(child.hProcess, &exitCode);
    CloseHandle(child.hProcess);
    return static_cast<int>(exitCode);
}
