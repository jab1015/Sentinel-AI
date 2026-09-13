#include <windows.h>
#include <appmodel.h>
#include <shlobj.h>
#include <shobjidl_core.h>
#include <wrl/client.h>

#include <atomic>
#include <chrono>
#include <cstdint>
#include <filesystem>
#include <sstream>
#include <string>
#include <vector>

#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "shell32.lib")

using Microsoft::WRL::ComPtr;

namespace
{
    constexpr UINT kMaximumItems = 16;
    constexpr size_t kMaximumRecordBytes = 64 * 1024;
    constexpr size_t kMaximumPathCharacters = 32767;
    constexpr wchar_t kRootTitle[] = L"Sentinel AI";
    constexpr wchar_t kArgumentName[] = L"--sentinel-explorer-handoff";

    const CLSID CLSID_SentinelExplorerCommand =
        { 0x6c5e88b7, 0x2a44, 0x4b6d, { 0x9a, 0x6c, 0x4f, 0x1a, 0x5c, 0x9f, 0x6e, 0x21 } };

    struct CommandDefinition
    {
        const wchar_t* title;
        const char* command;
        bool requiresSingleFile;
    };

    constexpr CommandDefinition kCommands[] =
    {
        { L"Inspect with Sentinel AI", "inspect", false },
        { L"Encrypt File", "encrypt", true },
        { L"Decrypt Sentinel File", "decrypt", true },
        { L"Add to Sentinel Vault", "vault", true },
        { L"Secure Delete", "secure-delete", true }
    };

    std::atomic<long> g_objectCount{ 0 };

    HRESULT DuplicateString(const wchar_t* value, LPWSTR* output)
    {
        if (output == nullptr) return E_POINTER;
        *output = nullptr;
        if (value == nullptr) return E_INVALIDARG;
        const size_t characters = wcslen(value) + 1;
        const size_t bytes = characters * sizeof(wchar_t);
        auto* copy = static_cast<wchar_t*>(CoTaskMemAlloc(bytes));
        if (copy == nullptr) return E_OUTOFMEMORY;
        memcpy(copy, value, bytes);
        *output = copy;
        return S_OK;
    }

    HRESULT CollectFilesystemPaths(IShellItemArray* items, std::vector<std::wstring>& paths)
    {
        paths.clear();
        if (items == nullptr) return E_INVALIDARG;
        DWORD count = 0;
        HRESULT hr = items->GetCount(&count);
        if (FAILED(hr)) return hr;
        if (count == 0 || count > kMaximumItems) return E_INVALIDARG;
        paths.reserve(count);
        for (DWORD index = 0; index < count; ++index)
        {
            ComPtr<IShellItem> item;
            hr = items->GetItemAt(index, &item);
            if (FAILED(hr)) return hr;
            PWSTR rawPath = nullptr;
            hr = item->GetDisplayName(SIGDN_FILESYSPATH, &rawPath);
            if (FAILED(hr) || rawPath == nullptr)
            {
                if (rawPath != nullptr) CoTaskMemFree(rawPath);
                return E_INVALIDARG;
            }
            std::wstring path(rawPath);
            CoTaskMemFree(rawPath);
            if (path.empty() || path.size() > kMaximumPathCharacters) return E_INVALIDARG;
            paths.push_back(std::move(path));
        }
        return S_OK;
    }

    bool IsSingleNormalFileSelection(IShellItemArray* items)
    {
        std::vector<std::wstring> paths;
        if (FAILED(CollectFilesystemPaths(items, paths)) || paths.size() != 1) return false;
        DWORD attributes = GetFileAttributesW(paths[0].c_str());
        return attributes != INVALID_FILE_ATTRIBUTES &&
            (attributes & FILE_ATTRIBUTE_DIRECTORY) == 0 &&
            (attributes & FILE_ATTRIBUTE_REPARSE_POINT) == 0;
    }

    HRESULT GetPackageFamily(std::wstring& family)
    {
        family.clear();
        UINT32 length = 0;
        LONG result = GetCurrentPackageFamilyName(&length, nullptr);
        if (result != ERROR_INSUFFICIENT_BUFFER || length == 0)
            return HRESULT_FROM_WIN32(result == ERROR_SUCCESS ? ERROR_INVALID_DATA : result);
        std::wstring buffer(length, L'\0');
        result = GetCurrentPackageFamilyName(&length, buffer.data());
        if (result != ERROR_SUCCESS) return HRESULT_FROM_WIN32(result);
        if (!buffer.empty() && buffer.back() == L'\0') buffer.pop_back();
        if (buffer.empty()) return E_UNEXPECTED;
        family = std::move(buffer);
        return S_OK;
    }

    HRESULT CreateHandoffDirectory(const std::wstring& family, std::filesystem::path& directory)
    {
        PWSTR localAppDataRaw = nullptr;
        HRESULT hr = SHGetKnownFolderPath(FOLDERID_LocalAppData, KF_FLAG_DEFAULT, nullptr, &localAppDataRaw);
        if (FAILED(hr)) return hr;
        std::filesystem::path localAppData(localAppDataRaw);
        CoTaskMemFree(localAppDataRaw);
        directory = localAppData / L"Packages" / family / L"LocalState" / L"ExplorerHandoff";
        std::error_code error;
        std::filesystem::create_directories(directory, error);
        if (error) return HRESULT_FROM_WIN32(error.value());
        return S_OK;
    }

    HRESULT CreateToken(std::wstring& token)
    {
        GUID value{};
        HRESULT hr = CoCreateGuid(&value);
        if (FAILED(hr)) return hr;
        wchar_t buffer[40]{};
        if (StringFromGUID2(value, buffer, static_cast<int>(std::size(buffer))) <= 0) return E_UNEXPECTED;
        token.clear();
        for (wchar_t character : std::wstring(buffer))
        {
            if (character == L'{' || character == L'}' || character == L'-') continue;
            token.push_back(character);
        }
        return token.size() == 32 ? S_OK : E_UNEXPECTED;
    }

    std::string Utf8FromWide(const std::wstring& value)
    {
        if (value.empty()) return {};
        const int length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(),
            static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
        if (length <= 0) return {};
        std::string output(static_cast<size_t>(length), '\0');
        if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, value.data(),
            static_cast<int>(value.size()), output.data(), length, nullptr, nullptr) != length) return {};
        return output;
    }

    std::string EscapeJson(const std::string& value)
    {
        std::ostringstream output;
        for (unsigned char character : value)
        {
            switch (character)
            {
            case '"': output << "\\\""; break;
            case '\\': output << "\\\\"; break;
            case '\b': output << "\\b"; break;
            case '\f': output << "\\f"; break;
            case '\n': output << "\\n"; break;
            case '\r': output << "\\r"; break;
            case '\t': output << "\\t"; break;
            default:
                if (character < 0x20)
                {
                    static constexpr char hex[] = "0123456789abcdef";
                    output << "\\u00" << hex[(character >> 4) & 0x0F] << hex[character & 0x0F];
                }
                else output << static_cast<char>(character);
                break;
            }
        }
        return output.str();
    }

    HRESULT BuildPayload(const std::vector<std::wstring>& paths, const char* command, std::string& payload)
    {
        if (command == nullptr || *command == '\0') return E_INVALIDARG;
        const auto now = std::chrono::time_point_cast<std::chrono::milliseconds>(std::chrono::system_clock::now());
        const auto createdUnixMs = now.time_since_epoch().count();
        std::ostringstream json;
        json << "{\"Version\":1,\"Command\":\"" << command << "\",\"CreatedUnixMs\":" << createdUnixMs << ",\"Items\":[";
        for (size_t index = 0; index < paths.size(); ++index)
        {
            std::string utf8 = Utf8FromWide(paths[index]);
            if (utf8.empty() && !paths[index].empty()) return HRESULT_FROM_WIN32(ERROR_NO_UNICODE_TRANSLATION);
            if (index != 0) json << ',';
            json << '"' << EscapeJson(utf8) << '"';
        }
        json << "]}";
        payload = json.str();
        return payload.empty() || payload.size() > kMaximumRecordBytes
            ? HRESULT_FROM_WIN32(ERROR_BUFFER_OVERFLOW)
            : S_OK;
    }

    HRESULT WriteHandoffFile(const std::filesystem::path& directory, const std::wstring& token,
        const std::string& payload, std::filesystem::path& filePath)
    {
        filePath = directory / (token + L".json");
        HANDLE file = CreateFileW(filePath.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW,
            FILE_ATTRIBUTE_TEMPORARY | FILE_ATTRIBUTE_NOT_CONTENT_INDEXED, nullptr);
        if (file == INVALID_HANDLE_VALUE) return HRESULT_FROM_WIN32(GetLastError());
        HRESULT result = S_OK;
        DWORD written = 0;
        if (!WriteFile(file, payload.data(), static_cast<DWORD>(payload.size()), &written, nullptr) || written != payload.size())
            result = HRESULT_FROM_WIN32(GetLastError());
        else if (!FlushFileBuffers(file))
            result = HRESULT_FROM_WIN32(GetLastError());
        CloseHandle(file);
        if (FAILED(result)) DeleteFileW(filePath.c_str());
        return result;
    }

    HRESULT ActivateSentinel(const std::wstring& family, const std::wstring& token)
    {
        ComPtr<IApplicationActivationManager> activationManager;
        HRESULT hr = CoCreateInstance(CLSID_ApplicationActivationManager, nullptr, CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(&activationManager));
        if (FAILED(hr)) return hr;
        const std::wstring appUserModelId = family + L"!App";
        const std::wstring arguments = std::wstring(kArgumentName) + L" " + token;
        DWORD processId = 0;
        return activationManager->ActivateApplication(appUserModelId.c_str(), arguments.c_str(), AO_NONE, &processId);
    }

    HRESULT SendRequest(IShellItemArray* items, const CommandDefinition& definition)
    {
        std::vector<std::wstring> paths;
        HRESULT hr = CollectFilesystemPaths(items, paths);
        if (FAILED(hr)) return hr;
        if (definition.requiresSingleFile && !IsSingleNormalFileSelection(items)) return E_INVALIDARG;
        std::wstring family;
        hr = GetPackageFamily(family);
        if (FAILED(hr)) return hr;
        std::filesystem::path handoffDirectory;
        hr = CreateHandoffDirectory(family, handoffDirectory);
        if (FAILED(hr)) return hr;
        std::wstring token;
        hr = CreateToken(token);
        if (FAILED(hr)) return hr;
        std::string payload;
        hr = BuildPayload(paths, definition.command, payload);
        if (FAILED(hr)) return hr;
        std::filesystem::path handoffFile;
        hr = WriteHandoffFile(handoffDirectory, token, payload, handoffFile);
        if (FAILED(hr)) return hr;
        hr = ActivateSentinel(family, token);
        if (FAILED(hr)) DeleteFileW(handoffFile.c_str());
        return hr;
    }

    class ExplorerSubCommand final : public IExplorerCommand
    {
    public:
        explicit ExplorerSubCommand(size_t index) : _index(index) { ++g_objectCount; }
        ~ExplorerSubCommand() { --g_objectCount; }
        IFACEMETHODIMP QueryInterface(REFIID iid, void** object) override
        {
            if (object == nullptr) return E_POINTER;
            *object = nullptr;
            if (iid == IID_IUnknown || iid == IID_IExplorerCommand)
            {
                *object = static_cast<IExplorerCommand*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }
        IFACEMETHODIMP_(ULONG) AddRef() override { return static_cast<ULONG>(InterlockedIncrement(&_references)); }
        IFACEMETHODIMP_(ULONG) Release() override
        {
            ULONG remaining = static_cast<ULONG>(InterlockedDecrement(&_references));
            if (remaining == 0) delete this;
            return remaining;
        }
        IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* name) override { return DuplicateString(kCommands[_index].title, name); }
        IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override { if (!icon) return E_POINTER; *icon = nullptr; return E_NOTIMPL; }
        IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override { if (!tip) return E_POINTER; *tip = nullptr; return E_NOTIMPL; }
        IFACEMETHODIMP GetCanonicalName(GUID* commandName) override
        {
            if (!commandName) return E_POINTER;
            *commandName = CLSID_SentinelExplorerCommand;
            commandName->Data1 ^= static_cast<unsigned long>(_index + 1);
            return S_OK;
        }
        IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
        {
            if (!state) return E_POINTER;
            if (kCommands[_index].requiresSingleFile)
                *state = IsSingleNormalFileSelection(items) ? ECS_ENABLED : ECS_DISABLED;
            else
            {
                std::vector<std::wstring> paths;
                *state = SUCCEEDED(CollectFilesystemPaths(items, paths)) ? ECS_ENABLED : ECS_DISABLED;
            }
            return S_OK;
        }
        IFACEMETHODIMP Invoke(IShellItemArray* items, IBindCtx*) override { return SendRequest(items, kCommands[_index]); }
        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override { if (!flags) return E_POINTER; *flags = ECF_DEFAULT; return S_OK; }
        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override { if (!commands) return E_POINTER; *commands = nullptr; return E_NOTIMPL; }
    private:
        size_t _index;
        volatile LONG _references = 1;
    };

    class SubCommandEnumerator final : public IEnumExplorerCommand
    {
    public:
        explicit SubCommandEnumerator(ULONG index = 0) : _index(index) { ++g_objectCount; }
        ~SubCommandEnumerator() { --g_objectCount; }
        IFACEMETHODIMP QueryInterface(REFIID iid, void** object) override
        {
            if (!object) return E_POINTER;
            *object = nullptr;
            if (iid == IID_IUnknown || iid == IID_IEnumExplorerCommand)
            {
                *object = static_cast<IEnumExplorerCommand*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }
        IFACEMETHODIMP_(ULONG) AddRef() override { return static_cast<ULONG>(InterlockedIncrement(&_references)); }
        IFACEMETHODIMP_(ULONG) Release() override
        {
            ULONG remaining = static_cast<ULONG>(InterlockedDecrement(&_references));
            if (remaining == 0) delete this;
            return remaining;
        }
        IFACEMETHODIMP Next(ULONG count, IExplorerCommand** commands, ULONG* fetched) override
        {
            if (!commands || (count != 1 && fetched == nullptr)) return E_POINTER;
            ULONG produced = 0;
            while (produced < count && _index < static_cast<ULONG>(std::size(kCommands)))
            {
                auto* command = new (std::nothrow) ExplorerSubCommand(_index++);
                if (!command) return E_OUTOFMEMORY;
                commands[produced++] = command;
            }
            if (fetched) *fetched = produced;
            return produced == count ? S_OK : S_FALSE;
        }
        IFACEMETHODIMP Skip(ULONG count) override
        {
            ULONG remaining = static_cast<ULONG>(std::size(kCommands)) - _index;
            ULONG skipped = min(count, remaining);
            _index += skipped;
            return skipped == count ? S_OK : S_FALSE;
        }
        IFACEMETHODIMP Reset() override { _index = 0; return S_OK; }
        IFACEMETHODIMP Clone(IEnumExplorerCommand** clone) override
        {
            if (!clone) return E_POINTER;
            *clone = new (std::nothrow) SubCommandEnumerator(_index);
            return *clone ? S_OK : E_OUTOFMEMORY;
        }
    private:
        ULONG _index;
        volatile LONG _references = 1;
    };

    class ExplorerCommand final : public IExplorerCommand
    {
    public:
        ExplorerCommand() { ++g_objectCount; }
        ~ExplorerCommand() { --g_objectCount; }
        IFACEMETHODIMP QueryInterface(REFIID iid, void** object) override
        {
            if (!object) return E_POINTER;
            *object = nullptr;
            if (iid == IID_IUnknown || iid == IID_IExplorerCommand)
            {
                *object = static_cast<IExplorerCommand*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }
        IFACEMETHODIMP_(ULONG) AddRef() override { return static_cast<ULONG>(InterlockedIncrement(&_references)); }
        IFACEMETHODIMP_(ULONG) Release() override
        {
            ULONG remaining = static_cast<ULONG>(InterlockedDecrement(&_references));
            if (remaining == 0) delete this;
            return remaining;
        }
        IFACEMETHODIMP GetTitle(IShellItemArray*, LPWSTR* name) override { return DuplicateString(kRootTitle, name); }
        IFACEMETHODIMP GetIcon(IShellItemArray*, LPWSTR* icon) override { if (!icon) return E_POINTER; *icon = nullptr; return E_NOTIMPL; }
        IFACEMETHODIMP GetToolTip(IShellItemArray*, LPWSTR* tip) override { if (!tip) return E_POINTER; *tip = nullptr; return E_NOTIMPL; }
        IFACEMETHODIMP GetCanonicalName(GUID* commandName) override { if (!commandName) return E_POINTER; *commandName = CLSID_SentinelExplorerCommand; return S_OK; }
        IFACEMETHODIMP GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
        {
            if (!state) return E_POINTER;
            std::vector<std::wstring> paths;
            *state = SUCCEEDED(CollectFilesystemPaths(items, paths)) ? ECS_ENABLED : ECS_DISABLED;
            return S_OK;
        }
        IFACEMETHODIMP Invoke(IShellItemArray*, IBindCtx*) override { return E_NOTIMPL; }
        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) override
        {
            if (!flags) return E_POINTER;
            *flags = ECF_HASSUBCOMMANDS;
            return S_OK;
        }
        IFACEMETHODIMP EnumSubCommands(IEnumExplorerCommand** commands) override
        {
            if (!commands) return E_POINTER;
            *commands = new (std::nothrow) SubCommandEnumerator();
            return *commands ? S_OK : E_OUTOFMEMORY;
        }
    private:
        volatile LONG _references = 1;
    };

    class CommandClassFactory final : public IClassFactory
    {
    public:
        CommandClassFactory() { ++g_objectCount; }
        ~CommandClassFactory() { --g_objectCount; }
        IFACEMETHODIMP QueryInterface(REFIID iid, void** object) override
        {
            if (!object) return E_POINTER;
            *object = nullptr;
            if (iid == IID_IUnknown || iid == IID_IClassFactory)
            {
                *object = static_cast<IClassFactory*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }
        IFACEMETHODIMP_(ULONG) AddRef() override { return static_cast<ULONG>(InterlockedIncrement(&_references)); }
        IFACEMETHODIMP_(ULONG) Release() override
        {
            ULONG remaining = static_cast<ULONG>(InterlockedDecrement(&_references));
            if (remaining == 0) delete this;
            return remaining;
        }
        IFACEMETHODIMP CreateInstance(IUnknown* outer, REFIID iid, void** object) override
        {
            if (outer != nullptr) return CLASS_E_NOAGGREGATION;
            if (!object) return E_POINTER;
            auto* command = new (std::nothrow) CommandClassFactory();
            delete command;
            auto* explorerCommand = new (std::nothrow) ExplorerCommand();
            if (!explorerCommand) return E_OUTOFMEMORY;
            HRESULT hr = explorerCommand->QueryInterface(iid, object);
            explorerCommand->Release();
            return hr;
        }
        IFACEMETHODIMP LockServer(BOOL lock) override { if (lock) ++g_objectCount; else --g_objectCount; return S_OK; }
    private:
        volatile LONG _references = 1;
    };
}

extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID classId, REFIID iid, void** object)
{
    if (!IsEqualCLSID(classId, CLSID_SentinelExplorerCommand)) return CLASS_E_CLASSNOTAVAILABLE;
    if (!object) return E_POINTER;
    auto* factory = new (std::nothrow) CommandClassFactory();
    if (!factory) return E_OUTOFMEMORY;
    HRESULT hr = factory->QueryInterface(iid, object);
    factory->Release();
    return hr;
}

extern "C" HRESULT __stdcall DllCanUnloadNow()
{
    return g_objectCount.load() == 0 ? S_OK : S_FALSE;
}

BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(module);
    return TRUE;
}
