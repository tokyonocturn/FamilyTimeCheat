/*
 * FamilyTimeChef input helper.
 *
 *   ftinput.exe focus  <pid>            attach to the foreground thread and take focus
 *   ftinput.exe click  <x> <y>          real SendInput left click at screen coords
 *   ftinput.exe key    <pid> <vk>       real keystroke to that process' window
 *   ftinput.exe winrect <pid>           print the main window rect
 *
 * PowerShell's SetForegroundWindow silently fails when the caller does not own the
 * foreground, which is why the game never resumed. AttachThreadInput to the current
 * foreground thread makes the call actually take effect.
 */
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static HWND find_main(DWORD pid)
{
    HWND h = GetTopWindow(NULL);
    while (h) {
        DWORD owner = 0;
        GetWindowThreadProcessId(h, &owner);
        if (owner == pid && IsWindowVisible(h) && GetWindow(h, GW_OWNER) == NULL) {
            RECT r;
            if (GetWindowRect(h, &r) && (r.right - r.left) > 200 && (r.bottom - r.top) > 200)
                return h;
        }
        h = GetWindow(h, GW_HWNDNEXT);
    }
    return NULL;
}

static void force_foreground(HWND h)
{
    HWND fg = GetForegroundWindow();
    DWORD fg_thread = fg ? GetWindowThreadProcessId(fg, NULL) : 0;
    DWORD my_thread = GetCurrentThreadId();

    if (fg_thread && fg_thread != my_thread)
        AttachThreadInput(my_thread, fg_thread, TRUE);

    ShowWindow(h, SW_RESTORE);
    BringWindowToTop(h);
    SetForegroundWindow(h);
    SetFocus(h);

    if (fg_thread && fg_thread != my_thread)
        AttachThreadInput(my_thread, fg_thread, FALSE);
}

static void send_click(int x, int y)
{
    int sw = GetSystemMetrics(SM_CXSCREEN);
    int sh = GetSystemMetrics(SM_CYSCREEN);
    INPUT in;

    ZeroMemory(&in, sizeof(in));

    /* Three separate calls with real gaps: one batched SendInput can be consumed in a
       single frame, so the engine never observes a distinct press/release pair. */
    in.type = INPUT_MOUSE;
    in.mi.dwFlags = MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE;
    in.mi.dx = (LONG)((double)x * 65535.0 / (double)(sw > 1 ? sw - 1 : 1));
    in.mi.dy = (LONG)((double)y * 65535.0 / (double)(sh > 1 ? sh - 1 : 1));
    SendInput(1, &in, sizeof(INPUT));
    Sleep(120);

    ZeroMemory(&in, sizeof(in));
    in.type = INPUT_MOUSE;
    in.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
    SendInput(1, &in, sizeof(INPUT));
    Sleep(90);

    ZeroMemory(&in, sizeof(in));
    in.type = INPUT_MOUSE;
    in.mi.dwFlags = MOUSEEVENTF_LEFTUP;
    SendInput(1, &in, sizeof(INPUT));
    Sleep(60);
}

static void send_key(WORD vk)
{
    INPUT in[2];
    ZeroMemory(in, sizeof(in));

    in[0].type = INPUT_KEYBOARD;
    in[0].ki.wVk = vk;

    in[1].type = INPUT_KEYBOARD;
    in[1].ki.wVk = vk;
    in[1].ki.dwFlags = KEYEVENTF_KEYUP;

    SendInput(2, in, sizeof(INPUT));
}

int main(int argc, char **argv)
{
    if (argc >= 2 && _stricmp(argv[1], "fg") == 0) {
        HWND f = GetForegroundWindow();
        DWORD owner = 0;
        if (f) GetWindowThreadProcessId(f, &owner);
        printf("foreground hwnd=%p pid=%lu\n", (void *)f, owner);
        return 0;
    }

    if (argc < 3) {
        printf("usage: ftinput.exe focus|key|winrect <pid> | click <x> <y> | fg\n");
        return 2;
    }

    if (_stricmp(argv[1], "click") == 0) {
        if (argc < 4) { printf("usage: ftinput.exe click <x> <y>\n"); return 2; }
        send_click(atoi(argv[2]), atoi(argv[3]));
        printf("clicked %s,%s\n", argv[2], argv[3]);
        return 0;
    }

    if (_stricmp(argv[1], "hit") == 0) {
        if (argc < 4) { printf("usage: ftinput.exe hit <x> <y>\n"); return 2; }
        POINT p;
        p.x = atoi(argv[2]);
        p.y = atoi(argv[3]);
        HWND target = WindowFromPoint(p);
        if (!target) { printf("hit none\n"); return 3; }
        DWORD owner = 0;
        GetWindowThreadProcessId(target, &owner);
        char cls[128];
        cls[0] = 0;
        GetClassNameA(target, cls, sizeof(cls));
        RECT r;
        GetWindowRect(target, &r);
        printf("hit hwnd=%p pid=%lu class=%s rect=%ld,%ld,%ld,%ld\n",
               (void *)target, owner, cls, r.left, r.top, r.right, r.bottom);
        return 0;
    }

    if (_stricmp(argv[1], "fg") == 0) {
        HWND f = GetForegroundWindow();
        DWORD owner = 0;
        if (f) GetWindowThreadProcessId(f, &owner);
        printf("foreground hwnd=%p pid=%lu\n", (void *)f, owner);
        return 0;
    }

    DWORD pid = (DWORD)atoi(argv[2]);
    HWND h = find_main(pid);
    if (!h) { printf("no window for pid %lu\n", pid); return 3; }

    /* The game halts its whole process when it loses foreground, so a click that activates
       the GUI freezes the simulation and the command loop. WS_EX_NOACTIVATE lets a real
       mouse click land on the GUI's own controls without ever taking activation away. */
    if (_stricmp(argv[1], "nofocus") == 0) {
        LONG_PTR ex = GetWindowLongPtr(h, GWL_EXSTYLE);
        LONG_PTR want = (argc > 3 && _stricmp(argv[3], "off") == 0)
                        ? (ex & ~WS_EX_NOACTIVATE)
                        : (ex | WS_EX_NOACTIVATE);
        SetWindowLongPtr(h, GWL_EXSTYLE, want);
        SetWindowPos(h, NULL, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
        printf("exstyle 0x%llx -> 0x%llx on pid %lu\n",
               (unsigned long long)ex, (unsigned long long)GetWindowLongPtr(h, GWL_EXSTYLE), pid);
        return 0;
    }

    if (_stricmp(argv[1], "winrect") == 0) {
        RECT r;
        GetWindowRect(h, &r);
        printf("%ld %ld %ld %ld\n", r.left, r.top, r.right, r.bottom);
        return 0;
    }

    if (_stricmp(argv[1], "focus") == 0) {
        force_foreground(h);
        Sleep(400);
        printf("foreground now %p (wanted %p)\n", (void *)GetForegroundWindow(), (void *)h);
        return GetForegroundWindow() == h ? 0 : 4;
    }

    if (_stricmp(argv[1], "move") == 0) {
        if (argc < 5) { printf("usage: ftinput.exe move <pid> <x> <y> [w] [h]\n"); return 2; }
        RECT r;
        GetWindowRect(h, &r);
        int x = atoi(argv[3]);
        int y = atoi(argv[4]);
        int w = argc > 5 ? atoi(argv[5]) : (r.right - r.left);
        int hh = argc > 6 ? atoi(argv[6]) : (r.bottom - r.top);
        MoveWindow(h, x, y, w, hh, TRUE);
        GetWindowRect(h, &r);
        printf("moved to %ld,%ld %ldx%ld\n", r.left, r.top, r.right - r.left, r.bottom - r.top);
        return 0;
    }

    if (_stricmp(argv[1], "key") == 0) {
        if (argc < 4) { printf("usage: ftinput.exe key <pid> <vk>\n"); return 2; }
        force_foreground(h);
        Sleep(350);
        send_key((WORD)atoi(argv[3]));
        printf("sent vk %s to pid %lu\n", argv[3], pid);
        return 0;
    }

    printf("unknown command %s\n", argv[1]);
    return 2;
}
