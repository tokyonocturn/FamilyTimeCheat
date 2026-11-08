/*
 * FamilyTimeChef injector.
 *
 *   injector.exe <pid> <path-to-agent.dll>
 *
 * Classic LoadLibrary injection: allocate the path in the target, then run
 * LoadLibraryA on a remote thread. The agent does the Mono work itself.
 */
#include <windows.h>
#include <stdio.h>

int main(int argc, char **argv)
{
    DWORD pid;
    const char *dll;
    HANDLE proc, thread;
    void *remote;
    HMODULE kernel;
    FARPROC load_library;
    SIZE_T len;
    DWORD wait;

    if (argc < 3) {
        printf("usage: injector.exe <pid> <agent.dll>\n");
        return 2;
    }

    pid = (DWORD)atoi(argv[1]);
    dll = argv[2];
    len = strlen(dll) + 1;

    proc = OpenProcess(PROCESS_ALL_ACCESS, FALSE, pid);
    if (!proc) {
        printf("OpenProcess(%lu) failed: %lu\n", pid, GetLastError());
        return 3;
    }

    remote = VirtualAllocEx(proc, NULL, len, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remote) {
        printf("VirtualAllocEx failed: %lu\n", GetLastError());
        CloseHandle(proc);
        return 4;
    }

    if (!WriteProcessMemory(proc, remote, dll, len, NULL)) {
        printf("WriteProcessMemory failed: %lu\n", GetLastError());
        CloseHandle(proc);
        return 5;
    }

    kernel = GetModuleHandleA("kernel32.dll");
    load_library = GetProcAddress(kernel, "LoadLibraryA");

    thread = CreateRemoteThread(proc, NULL, 0,
                                (LPTHREAD_START_ROUTINE)load_library, remote, 0, NULL);
    if (!thread) {
        printf("CreateRemoteThread failed: %lu\n", GetLastError());
        CloseHandle(proc);
        return 6;
    }

    wait = WaitForSingleObject(thread, 15000);
    printf("injection %s (wait=%lu)\n", wait == WAIT_OBJECT_0 ? "completed" : "timed out", wait);

    CloseHandle(thread);
    CloseHandle(proc);
    return wait == WAIT_OBJECT_0 ? 0 : 7;
}
