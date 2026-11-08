/*
 * FamilyTimeChef native agent.
 *
 * Injected into the running game with LoadLibrary. It attaches its worker thread to
 * the game's live Mono scripting domain and invokes the managed shim there, so the
 * shim runs where mscorlib, UnityEngine and Assembly-CSharp are already valid.
 *
 * Every mono entry point is resolved with GetProcAddress: the mono import library is
 * not available, and the export set must be checked at runtime rather than assumed.
 */
#include <windows.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdarg.h>
#include <string.h>

typedef void *(__cdecl *p_thread_attach)(void *);
typedef void *(__cdecl *p_get_root_domain)(void);
typedef void *(__cdecl *p_assembly_get_image)(void *);
typedef void *(__cdecl *p_class_from_name)(void *, const char *, const char *);
typedef void *(__cdecl *p_class_get_method_from_name)(void *, const char *, int);
typedef void *(__cdecl *p_runtime_invoke)(void *, void *, void **, void **);
typedef void *(__cdecl *p_object_to_string)(void *, void **);
typedef char *(__cdecl *p_string_to_utf8)(void *);
typedef void (__cdecl *p_free)(void *);
typedef void *(__cdecl *p_image_open)(char *, unsigned int, int, unsigned int *, int, char *);
typedef void *(__cdecl *p_assembly_load)(void *, const char *, unsigned int *, int);
typedef void (*p_register_log)(void *);

static char g_dir[MAX_PATH];
static char g_log[MAX_PATH];

static void logline(const char *fmt, ...)
{
    FILE *f = fopen(g_log, "a");
    va_list ap;
    if (!f) return;
    va_start(ap, fmt);
    vfprintf(f, fmt, ap);
    va_end(ap);
    fputc('\n', f);
    fclose(f);
}

/*
 * Focus shield.
 *
 * The player halts its whole process the moment it stops being the foreground window:
 * Unity receives WM_ACTIVATEAPP(0)/WM_ACTIVATE(WA_INACTIVE) and, because the game ships
 * with runInBackground disabled, stops the player loop. Application.runInBackground is
 * exposed but its setter does not stick in this build (it reads back False), so the
 * reliable lever is to stop the engine from ever learning it was deactivated: subclass
 * the top-level window procedure in-process and swallow the deactivation messages.
 *
 * This must live in the injected agent (not the external tools): the replacement
 * procedure has to be a valid address inside the target process.
 */
static WNDPROC g_origProc;
static int     g_swallowed;

static LRESULT CALLBACK chef_wndproc(HWND h, UINT msg, WPARAM wp, LPARAM lp)
{
    if (msg == WM_ACTIVATEAPP && wp == 0) {
        if (g_swallowed < 8) { g_swallowed++; logline("focus shield: swallowed WM_ACTIVATEAPP(deactivate) #%d", g_swallowed); }
        return 0;
    }
    if (msg == WM_ACTIVATE && LOWORD(wp) == WA_INACTIVE) {
        if (g_swallowed < 8) { g_swallowed++; logline("focus shield: swallowed WM_ACTIVATE(WA_INACTIVE) #%d", g_swallowed); }
        return 0;
    }
    if (msg == WM_KILLFOCUS) return 0;
    if (msg == WM_NCACTIVATE && wp == 0) return TRUE;
    return CallWindowProcA(g_origProc, h, msg, wp, lp);
}

static BOOL CALLBACK find_main_cb(HWND h, LPARAM lp)
{
    DWORD pid = 0;
    RECT r;
    GetWindowThreadProcessId(h, &pid);
    if (pid != GetCurrentProcessId()) return TRUE;
    if (!IsWindowVisible(h)) return TRUE;
    if (GetWindow(h, GW_OWNER) != NULL) return TRUE;
    if (!GetWindowRect(h, &r)) return TRUE;
    if ((r.right - r.left) < 200 || (r.bottom - r.top) < 200) return TRUE;
    *(HWND *)lp = h;
    return FALSE;
}

static void install_focus_shield(void)
{
    HWND h = NULL;
    EnumWindows(find_main_cb, (LPARAM)&h);
    if (!h) { logline("focus shield: no main window found"); return; }

    g_origProc = (WNDPROC)SetWindowLongPtrA(h, GWLP_WNDPROC, (LONG_PTR)chef_wndproc);
    logline("focus shield installed on hwnd %p (orig proc %p, err %lu)",
            (void *)h, (void *)g_origProc, g_origProc ? 0UL : GetLastError());
}

static DWORD WINAPI worker(LPVOID unused)
{
    HMODULE mono;
    char shim[MAX_PATH];
    FILE *f;
    long n;
    char *buf;
    unsigned int status = 0;
    void *dom, *image, *asm_, *img, *klass, *method, *exc = NULL;
    p_get_root_domain get_root;
    p_thread_attach attach;
    p_image_open image_open;
    p_assembly_load assembly_load;
    p_assembly_get_image get_image;
    p_class_from_name class_from_name;
    p_class_get_method_from_name get_method;
    p_runtime_invoke runtime_invoke;
    p_object_to_string object_to_string;
    p_string_to_utf8 string_to_utf8;
    p_free mono_free;

    (void)unused;

    install_focus_shield();

    mono = GetModuleHandleA("mono-2.0-bdwgc.dll");
    logline("mono module = %p", (void *)mono);
    if (!mono) { logline("FAIL: mono runtime not loaded in this process"); return 1; }

    get_root       = (p_get_root_domain)GetProcAddress(mono, "mono_get_root_domain");
    attach         = (p_thread_attach)GetProcAddress(mono, "mono_thread_attach");
    image_open     = (p_image_open)GetProcAddress(mono, "mono_image_open_from_data_with_name");
    assembly_load  = (p_assembly_load)GetProcAddress(mono, "mono_assembly_load_from_full");
    get_image      = (p_assembly_get_image)GetProcAddress(mono, "mono_assembly_get_image");
    class_from_name= (p_class_from_name)GetProcAddress(mono, "mono_class_from_name");
    get_method     = (p_class_get_method_from_name)GetProcAddress(mono, "mono_class_get_method_from_name");
    runtime_invoke = (p_runtime_invoke)GetProcAddress(mono, "mono_runtime_invoke");
    object_to_string=(p_object_to_string)GetProcAddress(mono, "mono_object_to_string");
    string_to_utf8 = (p_string_to_utf8)GetProcAddress(mono, "mono_string_to_utf8");
    mono_free      = (p_free)GetProcAddress(mono, "mono_free");

    if (!get_root || !attach || !image_open || !assembly_load || !get_image ||
        !class_from_name || !get_method || !runtime_invoke) {
        logline("FAIL: required mono exports missing (root=%p attach=%p io=%p al=%p gi=%p cfn=%p gm=%p ri=%p)",
                (void *)get_root, (void *)attach, (void *)image_open, (void *)assembly_load,
                (void *)get_image, (void *)class_from_name, (void *)get_method, (void *)runtime_invoke);
        return 1;
    }

    /* Attach this thread to the live scripting domain. */
    dom = get_root();
    logline("root domain = %p", dom);
    attach(dom);
    logline("thread attached to domain");

    sprintf(shim, "%s\\FamilyTimeChef.InGame.dll", g_dir);
    logline("loading shim %s", shim);

    f = fopen(shim, "rb");
    if (!f) { logline("FAIL: cannot open shim"); return 1; }
    fseek(f, 0, SEEK_END);
    n = ftell(f);
    fseek(f, 0, SEEK_SET);
    buf = (char *)malloc((size_t)n);
    if (!buf) { fclose(f); logline("FAIL: alloc"); return 1; }
    if (fread(buf, 1, (size_t)n, f) != (size_t)n) { fclose(f); logline("FAIL: read"); return 1; }
    fclose(f);
    logline("shim size = %ld", n);

    status = 0;
    image = image_open(buf, (unsigned int)n, 1, &status, 0, shim);
    logline("image = %p status = %u", image, status);
    if (!image || status) { logline("FAIL: image load"); return 1; }

    status = 0;
    asm_ = assembly_load((void *)image, shim, &status, 0);
    logline("assembly = %p status = %u", asm_, status);
    if (!asm_) { logline("FAIL: assembly load"); return 1; }

    img = get_image(asm_);
    klass = class_from_name(img, "FamilyTimeChef.InGame", "Shim");
    logline("class = %p", klass);
    if (!klass) { logline("FAIL: FamilyTimeChef.InGame.Shim not found"); return 1; }

    method = get_method(klass, "Entry", 0);
    logline("entry method = %p", method);
    if (!method) { logline("FAIL: Shim.Entry() not found"); return 1; }

    runtime_invoke(method, NULL, NULL, &exc);
    logline("invoked; exception = %p", exc);

    if (exc && object_to_string && string_to_utf8) {
        char *s = string_to_utf8(object_to_string(exc, NULL));
        logline("EXCEPTION: %s", s ? s : "(unprintable)");
        if (mono_free) mono_free(s);
    }
    return 0;
}

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID reserved)
{
    char *slash;
    (void)reserved;

    if (reason == DLL_PROCESS_ATTACH) {
        GetModuleFileNameA(inst, g_dir, MAX_PATH);
        slash = strrchr(g_dir, '\\');
        if (slash) *slash = 0;
        sprintf(g_log, "%s\\agent.log", g_dir);
        logline("agent attached to pid %lu from %s", GetCurrentProcessId(), g_dir);
        DisableThreadLibraryCalls(inst);
        CreateThread(NULL, 0, worker, NULL, 0, NULL);
    }
    return TRUE;
}
