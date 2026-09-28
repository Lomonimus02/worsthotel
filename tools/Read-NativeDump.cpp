// Offline-only DbgEng reader. No attach, process-opening, process-control, or arbitrary
// debugger-command argument is exposed. The only debug target accepted is an MDMP file.
// Build with the installed x64 MSVC + Windows SDK: cl /EHsc /std:c++17 /MT
// Read-NativeDump.cpp /link dbgeng.lib ole32.lib
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <dbgeng.h>
#include <dbghelp.h>
#include <cstdio>
#include <cwchar>
#include <string>
#include <vector>

static std::string Utf8(const wchar_t* text)
{
    int size = WideCharToMultiByte(CP_UTF8, 0, text, -1, nullptr, 0, nullptr, nullptr);
    if (size <= 1) return {};
    std::vector<char> buffer(size);
    WideCharToMultiByte(CP_UTF8, 0, text, -1, buffer.data(), size, nullptr, nullptr);
    return std::string(buffer.data());
}

static std::wstring FullPath(const wchar_t* path)
{
    DWORD length = GetFullPathNameW(path, 0, nullptr, nullptr);
    if (!length) return {};
    std::vector<wchar_t> buffer(length);
    if (!GetFullPathNameW(path, length, buffer.data(), nullptr)) return {};
    return std::wstring(buffer.data());
}

class OutputSink final : public IDebugOutputCallbacks
{
    LONG references = 1;
    FILE* file;
public:
    explicit OutputSink(FILE* destination) : file(destination) {}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override
    {
        if (!value) return E_POINTER;
        *value = nullptr;
        if (iid != __uuidof(IUnknown) && iid != __uuidof(IDebugOutputCallbacks)) return E_NOINTERFACE;
        *value = static_cast<IDebugOutputCallbacks*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return InterlockedIncrement(&references); }
    ULONG STDMETHODCALLTYPE Release() override
    {
        ULONG count = InterlockedDecrement(&references);
        if (!count) delete this;
        return count;
    }
    HRESULT STDMETHODCALLTYPE Output(ULONG, PCSTR text) override
    {
        if (text) { fputs(text, file); fflush(file); }
        return S_OK;
    }
};

static bool Check(HRESULT result, const char* step, FILE* output)
{
    fprintf(output, "\n[%s HRESULT=0x%08lX]\n", step, static_cast<unsigned long>(result));
    fflush(output);
    if (SUCCEEDED(result)) return true;
    fprintf(stderr, "%s failed: 0x%08lX\n", step, static_cast<unsigned long>(result));
    return false;
}

int wmain(int argc, wchar_t** argv)
{
    if (argc < 3 || argc > 5)
    {
        fprintf(stderr, "Usage: Read-NativeDump.exe dump.dmp output.txt [symbol-path] [image-path]\n");
        return 2;
    }
    const std::wstring dump = FullPath(argv[1]), report = FullPath(argv[2]);
    if (dump.empty() || report.empty() || _wcsicmp(dump.c_str(), report.c_str()) == 0 ||
        dump.size() < 4 || _wcsicmp(dump.c_str() + dump.size() - 4, L".dmp") != 0)
    { fprintf(stderr, "A distinct .dmp input and output file are required.\n"); return 2; }
    FILE* input = nullptr;
    if (_wfopen_s(&input, dump.c_str(), L"rb") || !input)
    { fprintf(stderr, "Cannot open dump file.\n"); return 2; }
    char signature[4] = {};
    bool valid = fread(signature, 1, 4, input) == 4 && memcmp(signature, "MDMP", 4) == 0;
    fclose(input);
    if (!valid) { fprintf(stderr, "Input is not a Windows minidump (MDMP).\n"); return 2; }
    FILE* output = nullptr;
    if (_wfopen_s(&output, report.c_str(), L"wb") || !output)
    { fprintf(stderr, "Cannot create output report.\n"); return 2; }
    fprintf(output, "Offline native minidump analysis\nInput: %s\n", Utf8(dump.c_str()).c_str());
    fprintf(output, "No live process attachment. Symbol path: %s\nImage path: %s\n",
        argc >= 4 ? Utf8(argv[3]).c_str() : "(local only)", argc >= 5 ? Utf8(argv[4]).c_str() : "(dump paths)");

    HRESULT com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    IDebugClient4* client = nullptr;
    IDebugControl* control = nullptr;
    IDebugSymbols3* symbols = nullptr;
    auto sink = new OutputSink(output);
    int exitCode = 1;
    do
    {
        if (!Check(DebugCreate(__uuidof(IDebugClient4), reinterpret_cast<void**>(&client)), "DebugCreate", output)) break;
        if (!Check(client->QueryInterface(__uuidof(IDebugControl), reinterpret_cast<void**>(&control)), "IDebugControl", output)) break;
        if (!Check(client->QueryInterface(__uuidof(IDebugSymbols3), reinterpret_cast<void**>(&symbols)), "IDebugSymbols3", output)) break;
        if (!Check(client->SetOutputCallbacks(sink), "SetOutputCallbacks", output)) break;
        client->SetOutputMask(DEBUG_OUTPUT_NORMAL | DEBUG_OUTPUT_ERROR | DEBUG_OUTPUT_WARNING | DEBUG_OUTPUT_SYMBOLS);
        symbols->SetSymbolOptions(SYMOPT_DEFERRED_LOADS | SYMOPT_UNDNAME | SYMOPT_LOAD_LINES |
            SYMOPT_FAIL_CRITICAL_ERRORS | SYMOPT_NO_PROMPTS);
        if (!Check(symbols->SetSymbolPathWide(argc >= 4 ? argv[3] : L"."), "SetSymbolPath", output)) break;
        if (argc >= 5 && !Check(symbols->SetImagePathWide(argv[4]), "SetImagePath", output)) break;
        if (!Check(client->OpenDumpFileWide(dump.c_str(), 0), "OpenDumpFileWide", output)) break;
        if (!Check(control->WaitForEvent(0, 30000), "WaitForEvent", output)) break;
        // Fixed read-only commands. Missing optional analysis extensions are recorded,
        // while module lists and raw stack traces remain useful without public symbols.
        const char* commands[] = { ".time", "vertarget", "~", "lm", "~* kb", "!analyze -hang" };
        for (const char* command : commands)
        {
            fprintf(output, "\n========== %s ==========\n", command); fflush(output);
            Check(control->Execute(DEBUG_OUTCTL_THIS_CLIENT, command,
                DEBUG_EXECUTE_NOT_LOGGED | DEBUG_EXECUTE_NO_REPEAT), command, output);
        }
        exitCode = 0;
    } while (false);
    if (client) client->SetOutputCallbacks(nullptr);
    if (symbols) symbols->Release();
    if (control) control->Release();
    if (client) client->Release();
    sink->Release();
    fprintf(output, "\nReader exit code: %d\n", exitCode);
    fclose(output);
    if (SUCCEEDED(com)) CoUninitialize();
    printf("Offline report: %s\n", Utf8(report.c_str()).c_str());
    return exitCode;
}
