/*
 * Call of Cthulhu: Dark Corners of the Earth - HD Patch: CoCHD.asi
 * Loaded by the Ultimate ASI Loader (Engine\dinput8.dll) from Engine\scripts\, next to
 * ThirteenAG's widescreen fix. Settings: Engine\scripts\CoCHD.ini, read at game start.
 *
 * 1. Resolution: the game reads ScreenWidth/ScreenHeight/RefreshRate with SHGetValueA from
 *    HKCU\...\Call Of Cthulhu DCoTE\Settings. With [Display] Resolution=auto these reads return
 *    the monitor's own resolution, so the game always matches the screen, however it is started
 *    (launcher, GOG Galaxy, the exe). Width is capped at 3276: the HUD stores x*10 in a short and
 *    the inventory breaks beyond that; wider monitors get the largest mode of the same aspect
 *    ratio below the cap, scaled up by Direct3D.
 *
 * 2. Display mode: [Display] Mode = fullscreen | borderless | windowed. The device is always
 *    windowed: the game resets the device on every focus change without releasing its
 *    D3DPOOL_DEFAULT resources, so an exclusive device crashes on Alt+Tab (Reset fails with
 *    D3DERR_INVALIDCALL). "fullscreen" is a borderless window covering the monitor, which is
 *    switched to 60 Hz at its own resolution for as long as the game runs (temporary mode,
 *    Windows restores it when the game ends); borderless keeps the desktop refresh rate. Resets
 *    that would change nothing are answered without touching the device. The window is pinned
 *    (the game shrinks it to the back buffer otherwise), Direct3D scales the back buffer up.
 *    Unsupported MSAA levels are dropped instead of letting device creation fail.
 *
 * 3. 60 fps: physics and scripts break above 60 fps (crashes, stuck doors). Fullscreen at 60 Hz
 *    paces through vsync; everything else goes through a precise frame limiter (high
 *    resolution waitable timer + short spin) before Present.
 *
 * 4. Anisotropic filtering: [Display] Anisotropy=N turns linear minification into anisotropic.
 *
 * 5. Mouse look. DirectInput runs the mouse in exclusive mode, so the counts are raw (no
 *    Windows acceleration). The player view (CoCRotationController, "RotateSpeed" 400) then
 *    turns per frame by
 *        counts / 65535 * slider (0.2 .. 2.0) * 400 * (frame time * 60)   radians
 *    which is ~0.35 deg per count at the default slider: made for 400 DPI mice. With a modern
 *    mouse even the lowest slider step is far too fast and the steps are coarse. The frame
 *    time factor also makes the same hand movement turn more or less depending on the frame
 *    rate. Fixes (code patched in memory by signature, the exe stays untouched):
 *      a) frame time factor -> 1.0: every count turns the view by the same angle, always
 *      b) the four look-direction scales (1/65535) point to own values:
 *         [Mouse] Mode=sens  -> 0.022 deg x [Mouse] Sens per count, the Counter-Strike scale (slider ignored)
 *         [Mouse] Mode=cm360 -> exact centimetres per 360 deg at [Mouse] DPI (slider ignored)
 *         [Mouse] Mode=scale -> [Mouse] Scale x the game's own slider
 *
 * 6. Gamepad. The PC version only knows keyboard and mouse (the Xbox pad code was not ported).
 *    An XInput pad is read in the game's own DirectInput polls and turned into the keys and
 *    mouse buttons the player has bound (Documents\Bethesda\Call of Cthulhu\Options\
 *    Configuration.Data, re-read when it changes), so key bindings apply to the pad as well.
 *    In play the left stick walks, the right stick turns the view (degrees per second, through
 *    the same look scale as the mouse); in menus both sticks move the menu cursor, A clicks,
 *    B / Start go back. "In play" = the player's look controller ran within the last 300 ms.
 *
 * Diagnostics: [Debug] Log=1 writes Engine\scripts\CoCHD.log.
 *
 * Build (x86): see build.ps1
 */
#include <windows.h>
#include <mmsystem.h>
#include <d3d9.h>
#include <d3dcompiler.h>  /* types only; D3DCompile is loaded from the system DLL */
#include <xinput.h>
#include <setupapi.h>
#include <hidsdi.h>
#include <shlobj.h>
#include <stdio.h>
#include <stdlib.h>
#include <math.h>
#include "sony_pad.h"

#define HUD_MAX_WIDTH 3276

/* DirectInput's mouse state (dinput.h is not needed for anything else) */
typedef struct { LONG lX, lY, lZ; BYTE rgbButtons[4]; } DIMOUSESTATE;

/* ---- settings ------------------------------------------------------------------------------ */

enum { MODE_FULLSCREEN, MODE_BORDERLESS, MODE_WINDOWED };
enum { MOUSE_OFF, MOUSE_CM360, MOUSE_SCALE, MOUSE_SENS };

static struct {
    BOOL res_auto;
    DWORD width, height;      /* used when not auto */
    int mode;
    DWORD refresh;            /* 0 = auto */
    BOOL vsync;
    int fps;                  /* frame limit, 0 = off */
    int anisotropy;
    int mouse;
    double cm360, dpi, scale, yratio, sens;
    BOOL fix_frametime;
    BOOL pad;
    double pad_look, pad_deadzone, pad_curve, pad_cursor;
    BOOL pad_invert, pad_analog;
    BOOL game_gamma;          /* apply the game's gamma ramp ourselves (windowed devices ignore it) */
    BOOL log;
} cfg;

/* film grain on/off ([Display] FilmGrain), read by the naked grain_wrap */
static volatile LONG grain_on = 1;
/* [Debug] PostEffectsOff: bit n skips post-effect type n (experiments, see post_effect_wrap) */
static volatile LONG post_effects_off;

static HMODULE self;
static volatile LONG di_counts_x, di_counts_y, di_polls, di_keys, di_fail; /* diagnostics */
static char ini_path[MAX_PATH], log_path[MAX_PATH];
static FILE *log_file;

static void logm(const char *fmt, ...)
{
    va_list ap;

    if (!log_file)
        return;
    va_start(ap, fmt);
    vfprintf(log_file, fmt, ap);
    va_end(ap);
    fputc('\n', log_file);
    fflush(log_file);
}

static double ini_double(const char *sec, const char *key, double def)
{
    char buf[64];

    GetPrivateProfileStringA(sec, key, "", buf, sizeof(buf), ini_path);
    return buf[0] ? atof(buf) : def;
}

static void load_settings(void)
{
    char buf[64], *slash;

    GetModuleFileNameA(self, ini_path, MAX_PATH);
    slash = strrchr(ini_path, '\\');
    lstrcpyA(log_path, ini_path);
    lstrcpyA(slash ? slash + 1 : ini_path, "CoCHD.ini");
    slash = strrchr(log_path, '\\');
    lstrcpyA(slash ? slash + 1 : log_path, "CoCHD.log");

    GetPrivateProfileStringA("Display", "Resolution", "auto", buf, sizeof(buf), ini_path);
    cfg.res_auto = lstrcmpiA(buf, "auto") == 0;
    if (!cfg.res_auto && sscanf(buf, "%lux%lu", &cfg.width, &cfg.height) != 2)
        cfg.res_auto = TRUE;
    GetPrivateProfileStringA("Display", "Mode", "fullscreen", buf, sizeof(buf), ini_path);
    cfg.mode = lstrcmpiA(buf, "borderless") == 0 ? MODE_BORDERLESS : lstrcmpiA(buf, "windowed") == 0 ? MODE_WINDOWED : MODE_FULLSCREEN;
    GetPrivateProfileStringA("Display", "RefreshRate", "auto", buf, sizeof(buf), ini_path);
    cfg.refresh = lstrcmpiA(buf, "auto") == 0 ? 0 : (DWORD)atoi(buf);
    cfg.vsync = GetPrivateProfileIntA("Display", "VSync", 1, ini_path) != 0;
    cfg.fps = GetPrivateProfileIntA("Display", "FPSLimit", 60, ini_path);
    if (cfg.fps < 0 || cfg.fps > 60)
        cfg.fps = 60; /* the game is not stable above 60 */
    cfg.anisotropy = GetPrivateProfileIntA("Display", "Anisotropy", 16, ini_path);
    grain_on = GetPrivateProfileIntA("Display", "FilmGrain", 1, ini_path) != 0;
    cfg.game_gamma = GetPrivateProfileIntA("Display", "GameGamma", 1, ini_path) != 0;

    GetPrivateProfileStringA("Mouse", "Mode", "sens", buf, sizeof(buf), ini_path);
    cfg.mouse = lstrcmpiA(buf, "off") == 0 ? MOUSE_OFF : lstrcmpiA(buf, "scale") == 0 ? MOUSE_SCALE :
                lstrcmpiA(buf, "cm360") == 0 ? MOUSE_CM360 : MOUSE_SENS;
    cfg.sens = ini_double("Mouse", "Sens", 2.0);
    cfg.cm360 = ini_double("Mouse", "Cm360", 30.0);
    cfg.dpi = ini_double("Mouse", "DPI", 1600.0);
    cfg.scale = ini_double("Mouse", "Scale", 0.25);
    cfg.yratio = ini_double("Mouse", "YRatio", 1.0);
    cfg.fix_frametime = GetPrivateProfileIntA("Mouse", "FrameIndependent", 1, ini_path) != 0;
    if (cfg.sens <= 0) cfg.sens = 2.0;
    if (cfg.cm360 <= 0.5) cfg.cm360 = 30.0;
    if (cfg.dpi < 50) cfg.dpi = 1600.0;
    if (cfg.scale <= 0) cfg.scale = 1.0;
    if (cfg.yratio <= 0) cfg.yratio = 1.0;

    cfg.pad = GetPrivateProfileIntA("Pad", "Enabled", 1, ini_path) != 0;
    cfg.pad_look = ini_double("Pad", "LookSpeed", 200.0);
    cfg.pad_deadzone = ini_double("Pad", "Deadzone", 0.24);
    cfg.pad_curve = ini_double("Pad", "Curve", 2.0);
    cfg.pad_cursor = ini_double("Pad", "CursorSpeed", 0.0);
    cfg.pad_invert = GetPrivateProfileIntA("Pad", "InvertY", 0, ini_path) != 0;
    cfg.pad_analog = GetPrivateProfileIntA("Pad", "AnalogWalk", 1, ini_path) != 0;
    if (cfg.pad_look <= 0) cfg.pad_look = 200.0;
    if (cfg.pad_deadzone < 0.05 || cfg.pad_deadzone > 0.6) cfg.pad_deadzone = 0.24;
    if (cfg.pad_curve < 1.0 || cfg.pad_curve > 4.0) cfg.pad_curve = 2.0;

    cfg.log = GetPrivateProfileIntA("Debug", "Log", 0, ini_path) != 0;
    GetPrivateProfileStringA("Debug", "PostEffectsOff", "0", buf, sizeof(buf), ini_path);
    post_effects_off = (LONG)strtoul(buf, NULL, 0);
}

/* ---- helpers ------------------------------------------------------------------------------- */

static void write_code(void *at, const void *bytes, SIZE_T n)
{
    DWORD old;

    if (VirtualProtect(at, n, PAGE_EXECUTE_READWRITE, &old)) {
        memcpy(at, bytes, n);
        VirtualProtect(at, n, old, &old);
        FlushInstructionCache(GetCurrentProcess(), at, n);
    }
}

static void *patch_vtable(void *obj, int slot, void *hook)
{
    void **vtbl = *(void ***)obj;
    void *orig = vtbl[slot];

    if (orig == hook)
        return NULL;
    write_code(&vtbl[slot], &hook, sizeof(hook));
    return orig;
}

/* Replace an import of the game exe; returns the original function or NULL. */
static void *patch_iat(const char *dll, const char *name, void *hook)
{
    BYTE *base = (BYTE *)GetModuleHandleA(NULL);
    IMAGE_NT_HEADERS *nt = (IMAGE_NT_HEADERS *)(base + ((IMAGE_DOS_HEADER *)base)->e_lfanew);
    IMAGE_DATA_DIRECTORY dir = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
    IMAGE_IMPORT_DESCRIPTOR *imp;

    if (!dir.VirtualAddress)
        return NULL;
    for (imp = (IMAGE_IMPORT_DESCRIPTOR *)(base + dir.VirtualAddress); imp->Name; imp++) {
        IMAGE_THUNK_DATA *names, *funcs;

        if (lstrcmpiA((char *)(base + imp->Name), dll) != 0)
            continue;
        names = (IMAGE_THUNK_DATA *)(base + (imp->OriginalFirstThunk ? imp->OriginalFirstThunk : imp->FirstThunk));
        funcs = (IMAGE_THUNK_DATA *)(base + imp->FirstThunk);
        for (; names->u1.AddressOfData; names++, funcs++) {
            IMAGE_IMPORT_BY_NAME *ibn;
            void *orig;

            if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal))
                continue;
            ibn = (IMAGE_IMPORT_BY_NAME *)(base + names->u1.AddressOfData);
            if (lstrcmpA((char *)ibn->Name, name) != 0)
                continue;
            orig = (void *)funcs->u1.Function;
            write_code(&funcs->u1.Function, &hook, sizeof(hook));
            return orig;
        }
    }
    return NULL;
}

static BYTE *code_range(DWORD *size)
{
    BYTE *base = (BYTE *)GetModuleHandleA(NULL);
    IMAGE_NT_HEADERS *nt = (IMAGE_NT_HEADERS *)(base + ((IMAGE_DOS_HEADER *)base)->e_lfanew);

    *size = nt->OptionalHeader.SizeOfCode;
    return base + nt->OptionalHeader.BaseOfCode;
}

/* Byte pattern search; '?' in the mask = any byte. Returns the n-th match (0-based) or NULL. */
static BYTE *find(const char *pat, const char *mask, int nth)
{
    DWORD size, i, j, len = (DWORD)strlen(mask);
    BYTE *code = code_range(&size);

    for (i = 0; i + len <= size; i++) {
        for (j = 0; j < len; j++)
            if (mask[j] != '?' && code[i + j] != (BYTE)pat[j])
                break;
        if (j == len && nth-- == 0)
            return code + i;
    }
    return NULL;
}

static BYTE *call_target(BYTE *call)
{
    return call + 5 + *(LONG *)(call + 1);
}

/* singleton getters end with "mov eax, imm32; ret": the object's address */
static BYTE *singleton(BYTE *getter)
{
    int i;

    for (i = 0; i < 64; i++)
        if (getter[i] == 0xB8 && getter[i + 5] == 0xC3)
            return *(BYTE **)(getter + i + 1);
    return NULL;
}

/* ---- display mode selection ---------------------------------------------------------------- */

static DWORD mon_w, mon_h, mon_hz;   /* desktop mode of the primary monitor */
static DWORD game_w, game_h, game_hz;

static BOOL mode_exists(DWORD w, DWORD h, DWORD hz)
{
    DEVMODEA dm;
    DWORD i;

    for (i = 0; ZeroMemory(&dm, sizeof(dm)), dm.dmSize = sizeof(dm), EnumDisplaySettingsA(NULL, i, &dm); i++)
        if (dm.dmPelsWidth == w && dm.dmPelsHeight == h && (!hz || dm.dmDisplayFrequency == hz) && dm.dmBitsPerPel == 32)
            return TRUE;
    return FALSE;
}

static void choose_mode(void)
{
    DEVMODEA dm;
    DWORD i;

    ZeroMemory(&dm, sizeof(dm));
    dm.dmSize = sizeof(dm);
    EnumDisplaySettingsA(NULL, ENUM_CURRENT_SETTINGS, &dm);
    mon_w = dm.dmPelsWidth;
    mon_h = dm.dmPelsHeight;
    mon_hz = dm.dmDisplayFrequency;

    if (!cfg.res_auto) {
        game_w = cfg.width;
        game_h = cfg.height;
    } else if (mon_w <= HUD_MAX_WIDTH) {
        game_w = mon_w;
        game_h = mon_h;
    } else {
        /* largest mode with the monitor's aspect ratio that the HUD can handle */
        double aspect = (double)mon_w / mon_h;
        game_w = game_h = 0;
        for (i = 0; ZeroMemory(&dm, sizeof(dm)), dm.dmSize = sizeof(dm), EnumDisplaySettingsA(NULL, i, &dm); i++) {
            if (dm.dmPelsWidth > HUD_MAX_WIDTH || dm.dmBitsPerPel != 32 ||
                fabs((double)dm.dmPelsWidth / dm.dmPelsHeight - aspect) > 0.02)
                continue;
            if (dm.dmPelsWidth * dm.dmPelsHeight > game_w * game_h) {
                game_w = dm.dmPelsWidth;
                game_h = dm.dmPelsHeight;
            }
        }
        if (!game_w) {
            /* no such mode listed: an exact fraction of the monitor (Direct3D scales it) */
            game_w = mon_w / 2;
            game_h = mon_h / 2;
        }
    }

    /*
     * Fullscreen: the monitor keeps its resolution and runs at 60 Hz while the game is in front
     * (else a multiple of 60, paced by the limiter). The device itself is windowed, because the
     * game cannot recover a lost exclusive device (Alt+Tab: Reset fails, the game crashes).
     */
    game_hz = 0;
    if (cfg.mode == MODE_FULLSCREEN) {
        static const DWORD prefer[] = { 60, 120, 180, 240, 0 };
        if (cfg.refresh && mode_exists(mon_w, mon_h, cfg.refresh))
            game_hz = cfg.refresh;
        for (i = 0; !game_hz && prefer[i]; i++)
            if (mode_exists(mon_w, mon_h, prefer[i]))
                game_hz = prefer[i];
        if (!game_hz)
            game_hz = mon_hz;
    }
    logm("monitor %lux%lu@%lu -> game %lux%lu, display %luHz (%s)", mon_w, mon_h, mon_hz, game_w, game_h, game_hz,
         cfg.mode == MODE_FULLSCREEN ? "fullscreen" : cfg.mode == MODE_BORDERLESS ? "borderless" : "windowed");
}

/* fullscreen mode: switch the monitor to game_hz at game start (on) and back at exit (off) - never in between, a mode change loses the device */
static void set_display_refresh(BOOL on)
{
    static BOOL active;
    DEVMODEA dm;
    LONG r;

    if (cfg.mode != MODE_FULLSCREEN || !game_hz || game_hz == mon_hz || on == active)
        return;
    if (on) {
        ZeroMemory(&dm, sizeof(dm));
        dm.dmSize = sizeof(dm);
        dm.dmPelsWidth = mon_w;
        dm.dmPelsHeight = mon_h;
        dm.dmDisplayFrequency = game_hz;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
        /* CDS_FULLSCREEN: temporary, Windows restores the desktop mode when the game ends */
        r = ChangeDisplaySettingsExA(NULL, &dm, NULL, CDS_FULLSCREEN, NULL);
    } else {
        r = ChangeDisplaySettingsExA(NULL, NULL, NULL, 0, NULL);
    }
    active = on && r == DISP_CHANGE_SUCCESSFUL;
    logm("display refresh %s -> %ld", on ? "60 Hz mode" : "desktop", r);
}

/* ---- registry: resolution the game asks for ------------------------------------------------ */

typedef LSTATUS (WINAPI *SHGetValueA_t)(HKEY, LPCSTR, LPCSTR, DWORD *, void *, DWORD *);
static SHGetValueA_t real_SHGetValueA;

static LSTATUS WINAPI Hook_SHGetValueA(HKEY key, LPCSTR sub, LPCSTR value, DWORD *type, void *data, DWORD *size)
{
    LSTATUS r = real_SHGetValueA(key, sub, value, type, data, size);
    DWORD v;

    if (!sub || !value || !data || !strstr(sub, "Call Of Cthulhu DCoTE"))
        return r;
    if (lstrcmpiA(value, "ScreenWidth") == 0)
        v = game_w;
    else if (lstrcmpiA(value, "ScreenHeight") == 0)
        v = game_h;
    else if (lstrcmpiA(value, "RefreshRate") == 0 && game_hz)
        v = game_hz;
    else
        return r;
    *(DWORD *)data = v;
    if (type)
        *type = REG_DWORD;
    if (size)
        *size = sizeof(DWORD);
    logm("registry %s -> %lu", value, v);
    return ERROR_SUCCESS;
}

/* ---- frame limiter ------------------------------------------------------------------------- */

static LARGE_INTEGER qpc_freq;
static LONGLONG next_frame;
static HANDLE wait_timer;
static BOOL limiter_on;
static LONGLONG last_present;
static double frame_ms_max;  /* longest frame since the last log line (diagnostics) */

static void frame_stats(void)
{
    LARGE_INTEGER now;

    QueryPerformanceCounter(&now);
    if (last_present) {
        double ms = (double)(now.QuadPart - last_present) * 1000.0 / (double)qpc_freq.QuadPart;
        if (ms > frame_ms_max)
            frame_ms_max = ms;
    }
    last_present = now.QuadPart;
}

static void limit_frame(void)
{
    LARGE_INTEGER now;
    LONGLONG period;

    if (!limiter_on || cfg.fps <= 0)
        return;
    period = qpc_freq.QuadPart / cfg.fps;
    QueryPerformanceCounter(&now);
    if (next_frame == 0 || now.QuadPart - next_frame > period * 2) {
        /* first frame or a long stall (loading): restart the schedule instead of catching up */
        next_frame = now.QuadPart + period;
        return;
    }
    for (;;) {
        LONGLONG left;
        double ms;

        QueryPerformanceCounter(&now);
        left = next_frame - now.QuadPart;
        if (left <= 0)
            break;
        ms = (double)left * 1000.0 / (double)qpc_freq.QuadPart;
        if (ms > 2.0 && wait_timer) {
            LARGE_INTEGER due;
            due.QuadPart = -(LONGLONG)((ms - 1.2) * 10000.0);
            if (SetWaitableTimer(wait_timer, &due, 0, NULL, NULL, FALSE))
                WaitForSingleObject(wait_timer, 50);
        } else {
            YieldProcessor();
        }
    }
    next_frame += period;
}

/* ---- mouse look ---------------------------------------------------------------------------- */

static float look_mul_pitch = 1.0f / 65535.0f, look_mul_yaw = 1.0f / 65535.0f;
static BYTE *settings_obj;   /* game settings: int mouse slider at +0x14 (0..10) */
static BYTE *config_obj;     /* game config: float RotateSpeed at +0x1A0, movement speeds from +0x5B8 */
static BOOL frametime_fixed;
static BYTE *frametime_site;  /* the patched "fmul [60.0]" and its original bytes */
static BYTE frametime_orig[6];

/* the player rotation controller (pitch +0x2C, yaw +0x34 in radians), captured on its update:
   tells "in play" from menus/cutscenes (gamepad) and measures the view (diagnostics) */
static void *orig_look_update;
static BYTE *volatile look_ctrl;
static volatile DWORD last_look_tick;
static double yaw_sum, pitch_sum;
static float last_yaw, last_pitch;

static __declspec(naked) void look_update_wrapper(void)
{
    __asm {
        mov look_ctrl, ecx
        push ecx
        call GetTickCount
        mov last_look_tick, eax
        pop ecx
        jmp orig_look_update
    }
}

/* the game's timers (the look code uses no. 8): 20 bytes each from +0x0C, delta at +0, paused flag at +0x0C;
   menus and the pause screen stop it, while the look controller itself keeps being updated */
static BYTE *timers_obj;
/* diagnostics: distance the player walked (player object 0x7AB310 of the GOG exe, position at +0x40) */
static double walk_dist, walk_dx, walk_dy, walk_last_x, walk_last_y;
static int walk_frames;
#define LOOK_TIMER(t) (timers_obj + 0x0C + 8 * 20 + (t))

static BOOL in_play(void)
{
    if (!look_ctrl || GetTickCount() - last_look_tick >= 300)
        return FALSE;
    return !timers_obj || !*LOOK_TIMER(0x0C);
}

/* a menu is really open: the game paused (pause menu, inventory, journal) or no player view for 2 s (main menu,
   loading); a short gap of the look controller in play is neither play nor menu, so no button changes meaning */
static BOOL menu_open(void)
{
    if (timers_obj && *LOOK_TIMER(0x0C))
        return TRUE;
    return !look_ctrl || GetTickCount() - last_look_tick >= 2000;
}

static void track_look(void)
{
    BYTE *c = look_ctrl;
    float yaw, pitch;
    double d;

    if (!c)
        return;
    yaw = *(float *)(c + 0x34);
    pitch = *(float *)(c + 0x2C);
    d = yaw - last_yaw;
    while (d > 3.14159265) d -= 6.2831853;
    while (d < -3.14159265) d += 6.2831853;
    if (fabs(d) < 3.0)
        yaw_sum += d;
    pitch_sum += pitch - last_pitch;
    last_yaw = yaw;
    last_pitch = pitch;
}

/* in-game slider (0.2 .. 2.0) x RotateSpeed: radians per (counts x look scale) */
static double look_base(void)
{
    double slider = 1.0, rot = 400.0;

    if (settings_obj) {
        slider = *(int *)(settings_obj + 0x14) * 0.1;
        slider = (slider < 0 ? 0 : slider > 1 ? 1 : slider) * 2.0;
    }
    if (config_obj && *(float *)(config_obj + 0x1A0) > 0)
        rot = *(float *)(config_obj + 0x1A0);
    return slider * rot;
}

static void update_mouse(void)
{
    double base, target, mul;

    if (cfg.mouse == MOUSE_SCALE) {
        mul = cfg.scale / 65535.0;
    } else if ((cfg.mouse == MOUSE_CM360 || cfg.mouse == MOUSE_SENS) && settings_obj && config_obj) {
        /* game: rad per count = mul * slider * RotateSpeed. Wanted: sens -> 0.022 deg x sens per count
           (the Quake/Source/Counter-Strike scale, so a player's usual number feels the same);
           cm360 -> 2pi per (cm360 / 2.54 * dpi) counts */
        base = look_base();
        if (base <= 0 || *(float *)(config_obj + 0x1A0) <= 0)
            return;
        target = cfg.mouse == MOUSE_SENS ? cfg.sens * 0.022 * 3.141592653589793 / 180.0
                                         : 6.283185307179586 / (cfg.cm360 / 2.54 * cfg.dpi);
        mul = target / base;
    } else {
        return;
    }
    look_mul_yaw = (float)mul;
    look_mul_pitch = (float)(mul * cfg.yratio);
}

/* route the controller's virtual update through look_update_wrapper */
static void hook_look_vtable(BYTE *fn)
{
    BYTE *base = (BYTE *)GetModuleHandleA(NULL);
    IMAGE_NT_HEADERS *nt = (IMAGE_NT_HEADERS *)(base + ((IMAGE_DOS_HEADER *)base)->e_lfanew);
    IMAGE_SECTION_HEADER *sec = IMAGE_FIRST_SECTION(nt);
    void *wrapper = (void *)look_update_wrapper;
    int s;

    for (s = 0; s < nt->FileHeader.NumberOfSections; s++, sec++) {
        BYTE **p = (BYTE **)(base + sec->VirtualAddress), **end = (BYTE **)(base + sec->VirtualAddress + sec->Misc.VirtualSize);
        if (sec->Characteristics & IMAGE_SCN_MEM_EXECUTE)
            continue;
        for (; p < end; p++) {
            if (*p == fn) {
                orig_look_update = fn;
                write_code(p, &wrapper, sizeof(wrapper));
                logm("look controller vtable slot %p", (void *)p);
                return;
            }
        }
    }
}

static void patch_mouse(void)
{
    BYTE *p, *site;
    float *orig_scale = NULL;
    int i, n = 0;
    static const BYTE fld1[6] = { 0xDD, 0xD8, 0xD9, 0xE8, 0x90, 0x90 }; /* fstp st(0); fld1; nop; nop */

    /* player look: push 8; call timers; mov ecx,eax; call delta; fmul [60.0]; fstp [esp+8]; call config; movss xmm0,[eax+1A0] */
    p = find("\x6A\x08\xE8????\x8B\xC8\xE8????\xD8\x0D????\xD9\x5C\x24\x08\xE8????\xF3\x0F\x10\x80\xA0\x01\x00\x00",
             "xxx????xxx????xx????xxxxx????xxxxxxxx", 0);
    if (p) {
        config_obj = singleton(call_target(p + 24));
        timers_obj = singleton(call_target(p + 2));
        if (cfg.mouse != MOUSE_OFF && cfg.fix_frametime && *(float *)*(float **)(p + 16) == 60.0f) {
            frametime_site = p + 14;
            memcpy(frametime_orig, p + 14, 6);
            write_code(p + 14, fld1, sizeof(fld1));
            frametime_fixed = TRUE;
        }
        /* the function starts with: sub esp,..; push esi; push edi; mov esi,ecx; call settings */
        for (i = 0; i < 0x100; i++) {
            BYTE *q = p - i;
            if (q[0] == 0x83 && q[1] == 0xEC && q[5] == 0x8B && q[6] == 0xF1 && q[7] == 0xE8) {
                settings_obj = singleton(call_target(q + 7));
                if (log_file || cfg.pad)
                    hook_look_vtable(q);
                break;
            }
        }
    }
    logm("look code %p: frame time fix %s, config %p, settings %p", (void *)p, frametime_fixed ? "on" : "off",
         (void *)config_obj, (void *)settings_obj);

    if (cfg.mouse == MOUSE_OFF)
        return;
    /*
     * input value -> option value: fild [esp+50]; jge; fadd [2^32]; fmul [scale]; push ecx; fstp [esp]; call options; mov ecx,eax; call setter
     * Look directions (+14..+20) use 1/65535. (The walk directions use 1/32767, but scaling those does not change
     * the speed - the movement code normalises the direction; the pad scales the speed itself, see set_walk_speed.)
     */
    for (i = 0; i < 32 && (site = find("\xDB\x44\x24\x50\x7D\x06\xD8\x05????\xD8\x0D????\x51\xD9\x1C\x24\xE8????\x8B\xC8\xE8",
                                       "xxxxxxxx????xx????xxxxx????xxx", i)) != NULL; i++) {
        float **ref = (float **)(site + 14);
        BYTE *setter = call_target(site + 29);
        int j, field = -1;
        float *target = NULL;
        BOOL look = fabs(**ref * 65535.0f - 1.0f) < 1e-4f;

        if (!look)
            continue;
        if (!orig_scale)
            orig_scale = *ref;
        if (*ref != orig_scale)
            continue;
        /* the setter stores into the options object: movss [ecx+XX], xmm0 */
        for (j = 0; j < 48; j++)
            if (setter[j] == 0xF3 && setter[j + 1] == 0x0F && setter[j + 2] == 0x11 && (setter[j + 3] & 0xC7) == 0x41) {
                field = setter[j + 4];
                break;
            }
        target = field == 0x14 || field == 0x18 ? &look_mul_pitch : field == 0x1C || field == 0x20 ? &look_mul_yaw : NULL;
        if (!target)
            continue;
        write_code(ref, &target, sizeof(target));
        logm("input scale site %p -> field +%02X", (void *)site, field);
        n++;
    }
    update_mouse();
    logm("mouse: mode %d, sens %.2f, %d input scale sites patched", cfg.mouse, cfg.sens, n);
}

/* ---- gamepad (XInput -> the player's own key bindings) ------------------------------------- */

typedef DWORD (WINAPI *XInputGetState_t)(DWORD, XINPUT_STATE *);
static XInputGetState_t xinput_get;
static int pad_index = -1;
static DWORD pad_retry, pad_last_read;
static XINPUT_STATE pad_state;
static WORD pad_prev_buttons;
static BYTE pad_kbd[256];        /* DirectInput key codes the pad holds */
static BYTE pad_btn[4];          /* mouse buttons the pad holds */
static int pad_wheel;            /* wheel steps to deliver */
static double pad_dx, pad_dy;    /* mouse counts to deliver (fractions kept) */
static LARGE_INTEGER pad_last_mouse;
static int weapon_slot = -1;
static int pulse_text;           /* a key pressed for a short moment (weapon slots) */
static DWORD pulse_until;

/* text id of an action -> its binding (device 0 mouse / 1 keyboard, code); from Configuration.Data */
#define MAX_TEXT 700
static signed char bind_dev[MAX_TEXT];
static int bind_code[MAX_TEXT];
static char options_path[MAX_PATH];
static FILETIME options_time;
static DWORD options_check;

/* the game's own defaults, for actions the file does not list (or no file yet) */
static const int default_binds[][3] = {
    { 400, 1, 0x11 }, { 401, 1, 0x2D }, { 402, 1, 0x1E }, { 403, 1, 0x20 }, { 410, 1, 0x2E }, { 408, 1, 0x39 },
    { 526, 1, 0x2A }, { 596, 1, 0x2A }, { 416, 1, 0x10 }, { 417, 1, 0x12 }, { 595, 1, 0x21 }, { 418, 1, 0x1F },
    { 586, 1, 0x17 }, { 585, 1, 0x23 }, { 566, 1, 0x0E }, { 411, 0, 7 }, { 412, 0, 7 }, { 413, 0, 6 }, { 510, 0, 6 },
    { 424, 1, 0x13 }, { 605, 1, 0x2C }, { 600, 1, 0x02 }, { 601, 1, 0x03 }, { 599, 1, 0x04 }, { 597, 1, 0x05 },
    { 598, 1, 0x06 }, { 603, 1, 0x07 }, { 602, 1, 0x08 }, { 604, 1, 0x09 },
};
/* weapon slots 1..8 */
static const int weapon_texts[8] = { 600, 601, 599, 597, 598, 603, 602, 604 };

enum {
    T_FWD = 400, T_BACK = 401, T_LEFT = 402, T_RIGHT = 403, T_JUMP = 408, T_CROUCH = 410, T_AIM = 411,
    T_FIRE = 413, T_LEANL = 416, T_LEANR = 417, T_ACTION = 418, T_RELOAD = 424, T_SNEAK = 526, T_SKIP = 566,
    T_HEAL = 585, T_INVENTORY = 586, T_LOOK = 595, T_WEAPON = 605,
};

/*
 * Button layout in play: the Xbox version's default (manual, "Controller Configuration 1"): A action, X jump,
 * Y reload, B crouch, LT aim, RT fire, right stick click sneak, Back skip, Start pause, d-pad weapons, White quick
 * heal (here LB), Black inventory (here RB). The PC version leans with separate keys: d-pad left/right (on the
 * Xbox the left stick leaned in sneak mode). Actions follow the player's key bindings.
 */
static const struct { WORD button; int text; } pad_buttons[] = {
    { XINPUT_GAMEPAD_A, T_ACTION }, { XINPUT_GAMEPAD_B, T_CROUCH }, { XINPUT_GAMEPAD_X, T_JUMP },
    { XINPUT_GAMEPAD_Y, T_RELOAD }, { XINPUT_GAMEPAD_LEFT_SHOULDER, T_HEAL }, { XINPUT_GAMEPAD_RIGHT_SHOULDER, T_INVENTORY },
    { XINPUT_GAMEPAD_RIGHT_THUMB, T_SNEAK }, { XINPUT_GAMEPAD_BACK, T_SKIP },
    { XINPUT_GAMEPAD_DPAD_DOWN, T_WEAPON }, { XINPUT_GAMEPAD_DPAD_LEFT, T_LEANL }, { XINPUT_GAMEPAD_DPAD_RIGHT, T_LEANR },
};

static void load_bindings(void)
{
    WIN32_FILE_ATTRIBUTE_DATA a;
    HANDLE f;
    BYTE *b;
    DWORD size, got = 0, at;
    int i, n = 0;

    if (!options_path[0]) {
        for (i = 0; i < (int)(sizeof(default_binds) / sizeof(default_binds[0])); i++) {
            bind_dev[default_binds[i][0]] = (signed char)default_binds[i][1];
            bind_code[default_binds[i][0]] = default_binds[i][2];
        }
        if (FAILED(SHGetFolderPathA(NULL, CSIDL_PERSONAL, NULL, 0, options_path)))
            return;
        lstrcatA(options_path, "\\Bethesda\\Call of Cthulhu\\Options\\Configuration.Data");
    }
    if (!GetFileAttributesExA(options_path, GetFileExInfoStandard, &a) || CompareFileTime(&a.ftLastWriteTime, &options_time) == 0)
        return;
    options_time = a.ftLastWriteTime;
    f = CreateFileA(options_path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, 0, NULL);
    if (f == INVALID_HANDLE_VALUE)
        return;
    size = GetFileSize(f, NULL);
    b = size > 0x16A && size < 65536 ? (BYTE *)malloc(size) : NULL;
    if (b && ReadFile(f, b, size, &got, NULL) && got == size && *(DWORD *)b == size && *(DWORD *)(b + 4) == 0x87654321) {
        /* 17-byte records from 0x16A: u32 index, u8 device, u32 code, u32 flags, u32 text id */
        for (at = 0x16A; at + 17 <= size; at += 17) {
            int text = *(int *)(b + at + 13);
            if (text > 0 && text < MAX_TEXT && b[at + 4] <= 1) {
                bind_dev[text] = (signed char)b[at + 4];
                bind_code[text] = *(int *)(b + at + 5);
                n++;
            }
        }
    }
    free(b);
    CloseHandle(f);
    logm("pad: %d key bindings read", n);
}

static void hold(int text)
{
    int code;

    if (text <= 0 || text >= MAX_TEXT)
        return;
    code = bind_code[text];
    if (bind_dev[text] == 1 && code > 0 && code < 256)
        pad_kbd[code] = 0x80;
    else if (bind_dev[text] == 0 && code >= 6 && code <= 9)
        pad_btn[code - 6] = 0x80;
}

/* wheel bindings are single steps: only on the press */
static void press_edge(int text)
{
    if (text > 0 && text < MAX_TEXT && bind_dev[text] == 0 && (bind_code[text] == 2 || bind_code[text] == 5))
        pad_wheel += bind_code[text] == 5 ? 120 : -120;
    else
        hold(text);
}

static double stick(SHORT v)
{
    return v < 0 ? v / 32768.0 : v / 32767.0;
}

/* magnitude after the dead zone and response curve, 0..1, with the direction in *nx/*ny */
static double shape(double x, double y, double *nx, double *ny)
{
    double m = sqrt(x * x + y * y), f;

    if (m < cfg.pad_deadzone) {
        *nx = *ny = 0;
        return 0;
    }
    *nx = x / m;
    *ny = y / m;
    f = (m > 1 ? 1 : m - cfg.pad_deadzone) / (1 - cfg.pad_deadzone);
    return pow(f > 1 ? 1 : f, cfg.pad_curve);
}

/*
 * Walking speed. The player's movement (0x54D4D0 in the GOG exe) asks 0x53F5C0 for the speed: config MoveSpeedWalk
 * (+0x5BC, 380), MoveSpeedRun (+0x5C0, 560), crawl (+0x5B8, 180, blended towards walk/run) or +0x5C4, and multiplies
 * the normalised direction with it - a key is always full speed. While the pad walks, these four are scaled by the
 * stick's tilt (like on the Xbox); otherwise they hold the game's values. A value the game reloads is taken over.
 */
#define SPEED_FIELDS 4
static float speed_orig[SPEED_FIELDS], speed_set[SPEED_FIELDS];
static BOOL speed_scaled;

static void set_walk_speed(double k)
{
    float *s;
    int i;

    if (!config_obj)
        return;
    s = (float *)(config_obj + 0x5B8);
    if (k >= 0.999) {
        if (speed_scaled)
            for (i = 0; i < SPEED_FIELDS; i++)
                if (s[i] == speed_set[i])
                    s[i] = speed_orig[i];
        speed_scaled = FALSE;
        return;
    }
    for (i = 0; i < SPEED_FIELDS; i++) {
        if (!speed_scaled || s[i] != speed_set[i])
            speed_orig[i] = s[i];
        if (!(speed_orig[i] > 0 && speed_orig[i] < 100000))
            continue;
        speed_set[i] = (float)(speed_orig[i] * k);
        s[i] = speed_set[i];
    }
    speed_scaled = TRUE;
}

/*
 * Walking direction. The movement code reads forwards/backwards (options +04/+08) and the two strafe getters, each
 * 0..1; a key gives 1, so stick + keys alone only know 8 directions. While the pad walks, the getters' non-zero
 * values are replaced by the stick's share of that axis (the bigger axis 1, the other one tan of the angle):
 * any direction, like on the Xbox. Near the axes the stick snaps straight (PAD_SNAP), so walking straight ahead
 * does not need a perfectly straight push.
 */
#define PAD_SNAP 0.27   /* tan(15 deg) */
static void *orig_options_getter, *orig_strafe_a, *orig_strafe_b;
static volatile LONG pad_dir_on;
static float pad_dir_x = 1, pad_dir_y = 1;
static BOOL walk_dir_patched;

static __declspec(naked) void fwd_wrap(void)
{
    __asm {
        call dword ptr [orig_options_getter]
        movss xmm0, dword ptr [eax + 4]
        cmp pad_dir_on, 0
        je fwd_done
        xorps xmm1, xmm1
        comiss xmm0, xmm1
        jbe fwd_done
        movss xmm0, pad_dir_y
    fwd_done:
        ret
    }
}

static __declspec(naked) void back_wrap(void)
{
    __asm {
        call dword ptr [orig_options_getter]
        movss xmm0, dword ptr [eax + 8]
        cmp pad_dir_on, 0
        je back_done
        xorps xmm1, xmm1
        comiss xmm0, xmm1
        jbe back_done
        movss xmm0, pad_dir_y
    back_done:
        ret
    }
}

/* the strafe getters are thiscall and return on the FPU stack */
static __declspec(naked) void strafe_a_wrap(void)
{
    __asm {
        call dword ptr [orig_strafe_a]
        cmp pad_dir_on, 0
        je sa_done
        fldz
        fcomip st, st(1)
        jae sa_done
        fstp st(0)
        fld pad_dir_x
    sa_done:
        ret
    }
}

static __declspec(naked) void strafe_b_wrap(void)
{
    __asm {
        call dword ptr [orig_strafe_b]
        cmp pad_dir_on, 0
        je sb_done
        fldz
        fcomip st, st(1)
        jae sb_done
        fstp st(0)
        fld pad_dir_x
    sb_done:
        ret
    }
}

static void put_call(BYTE *at, void *to, int nops)
{
    BYTE code[16];
    int i;

    code[0] = 0xE8;
    *(LONG *)(code + 1) = (LONG)((BYTE *)to - (at + 5));
    for (i = 0; i < nops; i++)
        code[5 + i] = 0x90;
    write_code(at, code, 5 + nops);
}

/* player movement (0x54D4D0 in the GOG exe): call options; movss xmm0,[eax+4]; movss [esp+0C],xmm0; call options;
   movss xmm0,[eax+8]; movss [esp+14],xmm0; call options; mov ecx,eax; call strafe; fstp [esp+20]; ...; fstp [esp+1C] */
static void patch_walk_direction(void)
{
    BYTE *p = find("\xE8????\xF3\x0F\x10\x40\x04\xF3\x0F\x11\x44\x24\x0C\xE8????\xF3\x0F\x10\x40\x08\xF3\x0F\x11\x44\x24\x14"
                   "\xE8????\x8B\xC8\xE8????\xD9\x5C\x24\x20\xE8????\x8B\xC8\xE8????\xD9\x5C\x24\x1C",
                   "x????xxxxxxxxxxxx????xxxxxxxxxxxx????xxx????xxxxx????xxx????xxxx", 0);

    if (!p || call_target(p) != call_target(p + 16) || call_target(p) != call_target(p + 32))
        return;
    orig_options_getter = call_target(p);
    orig_strafe_a = call_target(p + 39);
    orig_strafe_b = call_target(p + 55);
    put_call(p, (void *)fwd_wrap, 5);
    put_call(p + 16, (void *)back_wrap, 5);
    put_call(p + 39, (void *)strafe_a_wrap, 0);
    put_call(p + 55, (void *)strafe_b_wrap, 0);
    walk_dir_patched = TRUE;
}

/*
 * Film grain. A post-effect (0x466040 in the GOG exe, the one with the 64x64 noise texture that 0x45FF60 refills
 * with rand() every frame) lays the noise over the picture: texture factor alpha (fader + 1) x 5 and a second pass
 * with fader x 43 + 5, the fader rising in some scenes. Its draw call is routed through grain_wrap; with
 * FilmGrain=0 the draw is skipped and reported as done. Switchable while the game runs (ini reload with Log=1).
 */
static void *orig_grain_draw;
static BOOL grain_patched;

static __declspec(naked) void grain_wrap(void)
{
    __asm {
        cmp grain_on, 0
        je grain_skip
        jmp dword ptr [orig_grain_draw]
    grain_skip:
        xor eax, eax        /* as when the noise texture is missing: nothing drawn */
        ret 0x14
    }
}

/* push ecx; push esi; fldcw [esp+28]; push edx; push edi; mov ecx,ebx; call draw; pop edi..ecx; ret 0C */
static void patch_film_grain(void)
{
    BYTE *p = find("\x51\x56\xD9\x6C\x24\x28\x52\x57\x8B\xCB\xE8????\x5F\x5E\x5B\x59\xC2\x0C\x00",
                   "xxxxxxxxxxx????xxxxxxx", 0);
    BYTE *target;

    if (!p)
        return;
    target = call_target(p + 10);
    /* the draw function starts with sub esp,26Ch */
    if (target[0] != 0x81 || target[1] != 0xEC || *(DWORD *)(target + 2) != 0x26C)
        return;
    orig_grain_draw = target;
    put_call(p + 10, (void *)grain_wrap, 0);
    grain_patched = TRUE;
}

/*
 * Post-effects (experiments, Log=1 only). The scene's post-effect manager renders every effect through vtable
 * slot 3 (thiscall, 3 args, ret 0Ch). The 15 effect classes of the GOG exe (factory 0x45BB60, types 0..14) are
 * routed through one wrapper that records which types are active and skips those set in [Debug] PostEffectsOff.
 */
static const DWORD post_effect_vtables[15] = {
    0x632350, 0x632338, 0x632320, 0x632308, 0x631FA8, 0x63228C, 0x6321F8, 0x6321C8,
    0x63217C, 0x632118, 0x632274, 0x63208C, 0x6320F0, 0x6320BC, 0x632064 };
static const DWORD post_effect_render[15] = {
    0x46BDE0, 0x46BC80, 0x46B950, 0x46B890, 0x464720, 0x46B5E0, 0x4699F0, 0x469260,
    0x468F10, 0x466AC0, 0x46A580, 0x466040, 0x4667B0, 0x466340, 0x4656B0 };
static void *post_effect_orig[15];
static volatile LONG post_effects_seen;

static void *__cdecl post_effect_lookup(void *self_)
{
    DWORD vt = *(DWORD *)self_;
    int i;

    for (i = 0; i < 15; i++)
        if (post_effect_vtables[i] == vt) {
            post_effects_seen |= 1 << i;
            return (post_effects_off & (1 << i)) ? NULL : post_effect_orig[i];
        }
    return NULL;
}

static __declspec(naked) void post_effect_wrap(void)
{
    __asm {
        push ecx
        push ecx
        call post_effect_lookup
        add esp, 4
        pop ecx
        test eax, eax
        jz pe_skip
        jmp eax
    pe_skip:
        xor eax, eax        /* what an effect returns when it has nothing to draw */
        ret 0x0C
    }
}

static void hook_post_effects(void)
{
    void *wrap = (void *)post_effect_wrap;
    int i;

    for (i = 0; i < 15; i++)
        if (IsBadReadPtr((void *)post_effect_vtables[i], 16) || ((DWORD *)post_effect_vtables[i])[3] != post_effect_render[i])
            return; /* not the GOG build */
    for (i = 0; i < 15; i++) {
        post_effect_orig[i] = (void *)post_effect_render[i];
        write_code(&((void **)post_effect_vtables[i])[3], &wrap, sizeof(wrap));
    }
    logm("post-effects: 15 types hooked (debug)");
}

/* menus: d-pad as arrow keys (repeat timing) and whether A means Enter (after the d-pad) or a click */
static WORD menu_dpad;
static DWORD menu_pulse_end, menu_next_pulse;
static BOOL menu_keys;

static void pad_compute(void)
{
    double speed = 1;
    LONG dir_on = 0;
    const XINPUT_GAMEPAD *g = &pad_state.Gamepad;
    WORD now_buttons = g->wButtons, pressed = now_buttons & ~pad_prev_buttons;
    DWORD now = GetTickCount();
    double lx = stick(g->sThumbLX), ly = stick(g->sThumbLY);
    int i;

    memset(pad_kbd, 0, sizeof(pad_kbd));
    memset(pad_btn, 0, sizeof(pad_btn));
    if (now_buttons & XINPUT_GAMEPAD_START)
        pad_kbd[0x01] = 0x80; /* Esc: pause menu / back */
    if (in_play()) {
        double m = sqrt(lx * lx + ly * ly);
        if (m >= cfg.pad_deadzone) {
            /* walking speed follows the tilt (linear after the dead zone); the circle is stretched to a
               square so a full diagonal is as fast as two keys, like on the keyboard */
            double f = ((m > 1 ? 1 : m) - cfg.pad_deadzone) / (1 - cfg.pad_deadzone);
            double nx = lx / m, ny = ly / m, big = fabs(nx) > fabs(ny) ? fabs(nx) : fabs(ny);
            double fx = fabs(nx) / big * f, fy = fabs(ny) / big * f;
            double on = cfg.pad_analog ? 0.08 : 0.25;  /* keys only: no accidental walking at the edge of the dead zone */
            /* straight ahead / sideways within 15 degrees */
            if (fx < fy * PAD_SNAP)
                fx = 0;
            else if (fy < fx * PAD_SNAP)
                fy = 0;
            if (fy > on)
                hold(ly > 0 ? T_FWD : T_BACK);
            if (fx > on)
                hold(lx > 0 ? T_RIGHT : T_LEFT);
            if (cfg.pad_analog && walk_dir_patched && (fx > on || fy > on)) {
                big = fx > fy ? fx : fy;
                pad_dir_x = (float)(fx / big);
                pad_dir_y = (float)(fy / big);
                dir_on = 1;
            }
            /* slowest creep about a quarter of the walking speed, full tilt the game's own speed */
            if (cfg.pad_analog && (fx > on || fy > on))
                speed = 0.2 + 0.8 * (f > 1 ? 1 : f);
        }
        for (i = 0; i < (int)(sizeof(pad_buttons) / sizeof(pad_buttons[0])); i++) {
            if (pressed & pad_buttons[i].button)
                press_edge(pad_buttons[i].text);
            else if (now_buttons & pad_buttons[i].button)
                hold(pad_buttons[i].text);
        }
        if (g->bLeftTrigger > 90)
            hold(T_AIM);
        if (g->bRightTrigger > 90)
            hold(T_FIRE);
        if (pressed & XINPUT_GAMEPAD_DPAD_UP) {
            /* next weapon: the next slot key (empty slots are simply skipped by the game) */
            weapon_slot = (weapon_slot + 1) % 8;
            pulse_text = weapon_texts[weapon_slot];
            pulse_until = now + 120;
        }
        if (pulse_text && now < pulse_until)
            hold(pulse_text);
    } else if (menu_open()) {
        /*
         * Menus: the d-pad works like the arrow keys (selects the entries, as the keyboard does), the sticks move
         * the pointer (pad_mouse). A confirms what was used last: Enter after the d-pad, a click after the
         * pointer. Start (above) goes back - B stays crouch only, so it can never open the menu.
         */
        static const struct { WORD button; BYTE dik; } arrows[4] = {
            { XINPUT_GAMEPAD_DPAD_UP, 0xC8 }, { XINPUT_GAMEPAD_DPAD_DOWN, 0xD0 },
            { XINPUT_GAMEPAD_DPAD_LEFT, 0xCB }, { XINPUT_GAMEPAD_DPAD_RIGHT, 0xCD } };
        WORD dpad = now_buttons & (XINPUT_GAMEPAD_DPAD_UP | XINPUT_GAMEPAD_DPAD_DOWN | XINPUT_GAMEPAD_DPAD_LEFT |
                                   XINPUT_GAMEPAD_DPAD_RIGHT);

        if (dpad) {
            /* one key press per step: on press, then repeated while held (400 ms, then every 120 ms) */
            if (dpad != menu_dpad) {
                menu_dpad = dpad;
                menu_pulse_end = now + 60;
                menu_next_pulse = now + 400;
            } else if ((LONG)(now - menu_next_pulse) >= 0) {
                menu_pulse_end = now + 60;
                menu_next_pulse = now + 120;
            }
            if ((LONG)(menu_pulse_end - now) > 0)
                for (i = 0; i < 4; i++)
                    if (dpad & arrows[i].button)
                        pad_kbd[arrows[i].dik] = 0x80;
            menu_keys = TRUE;
        } else
            menu_dpad = 0;
        if (now_buttons & XINPUT_GAMEPAD_A) {
            if (menu_keys)
                pad_kbd[0x1C] = 0x80;   /* Enter */
            else
                pad_btn[0] = 0x80;
        }
    } else
        menu_dpad = 0;
    set_walk_speed(speed);
    pad_dir_on = dir_on;
    if (pressed && log_file)
        logm("pad buttons %04x (%s)", pressed, in_play() ? "play" : "menu");
    pad_prev_buttons = now_buttons;
}

/* ---- PlayStation controllers (HID, see sony_pad.h) ------------------------------------------------- */

/*
 * A watcher thread looks for a DualShock 4 / DualSense every two seconds (enumerating HID devices takes too long
 * for the game thread) and starts a reader thread for it; the reader keeps the latest state as an XINPUT_STATE.
 * pad_get() offers it as slot 4 after the four XInput slots, so an Xbox pad (or Steam's / DS4Windows' virtual
 * one) wins when both are there.
 */
static CRITICAL_SECTION sony_lock;
static volatile LONG sony_alive, sony_have;
static XINPUT_STATE sony_state;
static BOOL sony_ds;
static USHORT sony_pid;

static DWORD WINAPI sony_reader(LPVOID param)
{
    HANDLE h = (HANDLE)param;
    BYTE buf[512];
    DWORD n;
    XINPUT_GAMEPAD g;

    while (ReadFile(h, buf, sizeof(buf), &n, NULL)) {
        if (!sony_parse(buf, n, sony_ds, &g))
            continue;
        EnterCriticalSection(&sony_lock);
        sony_state.Gamepad = g;
        sony_state.dwPacketNumber++;
        sony_have = 1;
        LeaveCriticalSection(&sony_lock);
    }
    CloseHandle(h);
    sony_have = 0;
    sony_alive = 0;
    return 0;
}

static BOOL sony_open(void)
{
    GUID guid;
    HDEVINFO set;
    SP_DEVICE_INTERFACE_DATA ifd;
    DWORD i;
    BOOL found = FALSE;

    HidD_GetHidGuid(&guid);
    set = SetupDiGetClassDevsA(&guid, NULL, NULL, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
    if (set == INVALID_HANDLE_VALUE)
        return FALSE;
    ifd.cbSize = sizeof(ifd);
    for (i = 0; !found && SetupDiEnumDeviceInterfaces(set, NULL, &guid, i, &ifd); i++) {
        union { SP_DEVICE_INTERFACE_DETAIL_DATA_A d; BYTE raw[1024]; } det;
        HIDD_ATTRIBUTES at;
        HANDLE h, t;
        BOOL ds = FALSE, ok;

        det.d.cbSize = sizeof(det.d);
        if (!SetupDiGetDeviceInterfaceDetailA(set, &ifd, &det.d, sizeof(det), NULL, NULL))
            continue;
        /* no access rights needed to read the vendor and product ids */
        h = CreateFileA(det.d.DevicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, 0, NULL);
        if (h == INVALID_HANDLE_VALUE)
            continue;
        at.Size = sizeof(at);
        ok = HidD_GetAttributes(h, &at) && at.VendorID == SONY_VID && sony_pid_known(at.ProductID, &ds);
        CloseHandle(h);
        if (!ok)
            continue;
        h = CreateFileA(det.d.DevicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL,
                        OPEN_EXISTING, 0, NULL);
        if (h == INVALID_HANDLE_VALUE)
            h = CreateFileA(det.d.DevicePath, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_EXISTING, 0, NULL);
        if (h == INVALID_HANDLE_VALUE) {
            logm("PlayStation controller %04x found but not readable (%lu)", at.ProductID, GetLastError());
            continue;
        }
        sony_ds = ds;
        sony_pid = at.ProductID;
        sony_have = 0;
        sony_alive = 1;
        t = CreateThread(NULL, 0, sony_reader, h, 0, NULL);
        if (!t) {
            CloseHandle(h);
            sony_alive = 0;
            continue;
        }
        CloseHandle(t);
        logm("PlayStation controller found: %s (%04x)", ds ? "DualSense" : "DualShock 4", at.ProductID);
        found = TRUE;
    }
    SetupDiDestroyDeviceInfoList(set);
    return found;
}

static DWORD WINAPI sony_watcher(LPVOID unused)
{
    (void)unused;
    for (;;) {
        if (!sony_alive)
            sony_open();
        Sleep(2000);
    }
}

/* slots 0..3: XInput, slot 4: the PlayStation controller */
#define PAD_SLOTS 5
static DWORD pad_get(DWORD slot, XINPUT_STATE *st)
{
    if (slot < 4)
        return xinput_get ? xinput_get(slot, st) : ERROR_DEVICE_NOT_CONNECTED;
    if (!sony_alive || !sony_have)
        return ERROR_DEVICE_NOT_CONNECTED;
    EnterCriticalSection(&sony_lock);
    *st = sony_state;
    LeaveCriticalSection(&sony_lock);
    return ERROR_SUCCESS;
}

static void pad_read(void)
{
    DWORD now = GetTickCount();

    if (pad_last_read && now - pad_last_read < 4)
        return;
    pad_last_read = now;
    if (now - options_check > 2000) {
        options_check = now;
        load_bindings();
    }
    if (pad_index < 0) {
        int i;
        /* looking at empty XInput slots is slow: only every two seconds */
        if ((LONG)(now - pad_retry) < 0)
            return;
        pad_retry = now + 2000;
        for (i = 0; i < PAD_SLOTS; i++)
            if (pad_get(i, &pad_state) == ERROR_SUCCESS) {
                pad_index = i;
                pad_prev_buttons = pad_state.Gamepad.wButtons;
                logm("pad %d connected%s", i, i == 4 ? " (PlayStation controller)" : "");
                break;
            }
        if (pad_index < 0)
            return;
    }
    if (pad_get(pad_index, &pad_state) != ERROR_SUCCESS) {
        logm("pad %d disconnected", pad_index);
        pad_index = -1;
        set_walk_speed(1);
        pad_dir_on = 0;
        memset(pad_kbd, 0, sizeof(pad_kbd));
        memset(pad_btn, 0, sizeof(pad_btn));
        return;
    }
    pad_compute();
}

/* sticks -> mouse counts: view turn (degrees per second) in play, menu cursor (pixels per second) otherwise */
static void pad_mouse(DIMOUSESTATE *m)
{
    LARGE_INTEGER now;
    double dt, nx, ny, f, rad_per_count, lx, ly, rx, ry;
    int ix, iy, i;

    QueryPerformanceCounter(&now);
    dt = pad_last_mouse.QuadPart ? (double)(now.QuadPart - pad_last_mouse.QuadPart) / (double)qpc_freq.QuadPart : 0;
    pad_last_mouse = now;
    if (dt > 0.1)
        dt = 0.1;
    if (pad_index < 0)
        return;
    lx = stick(pad_state.Gamepad.sThumbLX);
    ly = stick(pad_state.Gamepad.sThumbLY);
    rx = stick(pad_state.Gamepad.sThumbRX);
    ry = stick(pad_state.Gamepad.sThumbRY);
    if (in_play()) {
        f = shape(rx, ry, &nx, &ny);
        if (f > 0) {
            double rad = cfg.pad_look * 3.141592653589793 / 180.0 * f * dt;
            rad_per_count = look_mul_yaw * look_base();
            if (rad_per_count > 0) {
                pad_dx += rad * nx / rad_per_count;
                /* stick up = look up = negative mouse Y */
                pad_dy += rad * (cfg.pad_invert ? ny : -ny) / (look_mul_pitch * look_base());
            }
        }
    } else if (menu_open()) {
        /* menu cursor: both sticks, full tilt crosses the screen height in about a second */
        double speed = cfg.pad_cursor > 0 ? cfg.pad_cursor : (game_h ? game_h : 1080) * 0.9;
        double sx = fabs(lx) > fabs(rx) ? lx : rx, sy = fabs(ly) > fabs(ry) ? ly : ry;
        f = shape(sx, sy, &nx, &ny);
        pad_dx += speed * f * nx * dt;
        pad_dy -= speed * f * ny * dt;
        if (f > 0 || m->lX || m->lY)
            menu_keys = FALSE; /* the pointer was moved: A clicks again */
    }
    ix = (int)pad_dx;
    iy = (int)pad_dy;
    pad_dx -= ix;
    pad_dy -= iy;
    m->lX += ix;
    m->lY += iy;
    m->lZ += pad_wheel;
    pad_wheel = 0;
    for (i = 0; i < 4; i++)
        m->rgbButtons[i] |= pad_btn[i];
}

/* ---- Direct3D 9 ---------------------------------------------------------------------------- */

typedef IDirect3D9 *(WINAPI *Direct3DCreate9_t)(UINT);
typedef HRESULT (STDMETHODCALLTYPE *CreateDevice_t)(IDirect3D9 *, UINT, D3DDEVTYPE, HWND, DWORD, D3DPRESENT_PARAMETERS *, IDirect3DDevice9 **);
typedef HRESULT (STDMETHODCALLTYPE *Reset_t)(IDirect3DDevice9 *, D3DPRESENT_PARAMETERS *);
typedef HRESULT (STDMETHODCALLTYPE *Present_t)(IDirect3DDevice9 *, const RECT *, const RECT *, HWND, const RGNDATA *);
typedef HRESULT (STDMETHODCALLTYPE *SetSamplerState_t)(IDirect3DDevice9 *, DWORD, D3DSAMPLERSTATETYPE, DWORD);

static Direct3DCreate9_t real_Direct3DCreate9;
static CreateDevice_t real_CreateDevice;
static Reset_t real_Reset;
static Present_t real_Present;
static SetSamplerState_t real_SetSamplerState;
typedef void (STDMETHODCALLTYPE *SetGammaRamp_t)(IDirect3DDevice9 *, UINT, DWORD, const D3DGAMMARAMP *);
static SetGammaRamp_t real_SetGammaRamp;
static volatile LONG gamma_calls;

/*
 * Gamma ramp. The game sets its brightness option and its screen flashes (lightning, shocks) as a gamma ramp
 * (IDirect3DDevice9::SetGammaRamp). Only an exclusive fullscreen device shows it; the patch always uses a windowed
 * one (the Alt+Tab crash), where Windows ignores it. So the ramp is applied here, as the graphics card would: before
 * Present the back buffer is copied and drawn back through a 256-entry lookup texture by a small pixel shader
 * (compiled with the system's d3dcompiler_47.dll). An identity ramp costs nothing. [Display] GameGamma=0 turns it off.
 */
static D3DGAMMARAMP game_ramp;
static volatile LONG ramp_dirty, ramp_identity = 1;
static IDirect3DTexture9 *gamma_copy, *gamma_lut;
static IDirect3DPixelShader9 *gamma_ps;
static IDirect3DStateBlock9 *gamma_state;
static UINT gamma_w, gamma_h;
static D3DFORMAT gamma_fmt;
static BOOL gamma_failed;

static const char gamma_hlsl[] =
    "sampler img : register(s0);\n"
    "sampler lut : register(s1);\n"
    "float4 main(float2 uv : TEXCOORD0) : COLOR {\n"
    "    float3 c = tex2D(img, uv).rgb * (255.0 / 256.0) + 0.5 / 256.0;\n"
    "    return float4(tex2D(lut, float2(c.r, 0.5)).r, tex2D(lut, float2(c.g, 0.5)).g, tex2D(lut, float2(c.b, 0.5)).b, 1);\n"
    "}\n";

/* the game sets a gamma ramp (brightness option, flashes) - keep it */
static void STDMETHODCALLTYPE Hook_SetGammaRamp(IDirect3DDevice9 *dev, UINT chain, DWORD flags, const D3DGAMMARAMP *r)
{
    static WORD last[5];
    WORD now[5];
    int i;
    LONG identity = 1;

    gamma_calls++;
    if (chain == 0 && r) {
        game_ramp = *r;
        for (i = 0; i < 256 && identity; i++)
            if ((r->red[i] >> 8) != i || (r->green[i] >> 8) != i || (r->blue[i] >> 8) != i)
                identity = 0;
        ramp_identity = identity;
        ramp_dirty = 1;
        if (log_file) {
            for (i = 0; i < 5; i++)
                now[i] = r->red[i == 4 ? 255 : i * 64];
            if (memcmp(now, last, sizeof(now))) {
                memcpy(last, now, sizeof(now));
                logm("gamma ramp: red 0/64/128/192/255 -> %u %u %u %u %u (green 128 %u, blue 128 %u)", now[0] >> 8, now[1] >> 8,
                     now[2] >> 8, now[3] >> 8, now[4] >> 8, r->green[128] >> 8, r->blue[128] >> 8);
            }
        }
    }
    real_SetGammaRamp(dev, chain, flags, r);
}

/* D3DPOOL_DEFAULT things and the state block must go before a real Reset */
static void gamma_release_default(void)
{
    if (gamma_copy)
        IDirect3DTexture9_Release(gamma_copy);
    if (gamma_state)
        IDirect3DStateBlock9_Release(gamma_state);
    gamma_copy = NULL;
    gamma_state = NULL;
}

static BOOL gamma_fail(const char *what, HRESULT hr)
{
    logm("gamma ramp emulation off: %s failed (%08lx)", what, (DWORD)hr);
    gamma_failed = TRUE;
    return FALSE;
}

static BOOL gamma_setup(IDirect3DDevice9 *dev, const D3DSURFACE_DESC *bd)
{
    HRESULT hr;

    if (!gamma_ps) {
        typedef HRESULT (WINAPI *D3DCompile_t)(LPCVOID, SIZE_T, LPCSTR, const D3D_SHADER_MACRO *, ID3DInclude *, LPCSTR,
                                               LPCSTR, UINT, UINT, ID3DBlob **, ID3DBlob **);
        HMODULE dc = LoadLibraryA("d3dcompiler_47.dll");
        D3DCompile_t compile = dc ? (D3DCompile_t)GetProcAddress(dc, "D3DCompile") : NULL;
        ID3DBlob *code = NULL, *err = NULL;

        if (!compile)
            return gamma_fail("loading d3dcompiler_47.dll", E_FAIL);
        hr = compile(gamma_hlsl, sizeof(gamma_hlsl) - 1, "gamma", NULL, NULL, "main", "ps_2_0", 0, 0, &code, &err);
        if (err)
            err->lpVtbl->Release(err);
        if (FAILED(hr))
            return gamma_fail("compiling the shader", hr);
        hr = IDirect3DDevice9_CreatePixelShader(dev, (const DWORD *)code->lpVtbl->GetBufferPointer(code), &gamma_ps);
        code->lpVtbl->Release(code);
        if (FAILED(hr))
            return gamma_fail("CreatePixelShader", hr);
        logm("gamma ramp emulation ready");
    }
    if (!gamma_lut) {
        hr = IDirect3DDevice9_CreateTexture(dev, 256, 1, 1, 0, D3DFMT_A8R8G8B8, D3DPOOL_MANAGED, &gamma_lut, NULL);
        if (FAILED(hr))
            return gamma_fail("lookup texture", hr);
        ramp_dirty = 1;
    }
    if (ramp_dirty) {
        D3DLOCKED_RECT lr;
        int i;

        ramp_dirty = 0;
        if (SUCCEEDED(IDirect3DTexture9_LockRect(gamma_lut, 0, &lr, NULL, 0))) {
            for (i = 0; i < 256; i++)
                ((DWORD *)lr.pBits)[i] = 0xFF000000 | (DWORD)(game_ramp.red[i] >> 8) << 16 |
                                         (DWORD)(game_ramp.green[i] >> 8) << 8 | (DWORD)(game_ramp.blue[i] >> 8);
            IDirect3DTexture9_UnlockRect(gamma_lut, 0);
        }
    }
    if (gamma_copy && (gamma_w != bd->Width || gamma_h != bd->Height || gamma_fmt != bd->Format))
        gamma_release_default();
    if (!gamma_copy) {
        hr = IDirect3DDevice9_CreateTexture(dev, bd->Width, bd->Height, 1, D3DUSAGE_RENDERTARGET, bd->Format,
                                            D3DPOOL_DEFAULT, &gamma_copy, NULL);
        if (FAILED(hr))
            return gamma_fail("copy texture", hr);
        gamma_w = bd->Width;
        gamma_h = bd->Height;
        gamma_fmt = bd->Format;
    }
    if (!gamma_state) {
        hr = IDirect3DDevice9_CreateStateBlock(dev, D3DSBT_ALL, &gamma_state);
        if (FAILED(hr))
            return gamma_fail("CreateStateBlock", hr);
    }
    return TRUE;
}

static void apply_gamma(IDirect3DDevice9 *dev)
{
    IDirect3DSurface9 *bb = NULL, *copy = NULL, *old_rt = NULL, *old_ds = NULL;
    D3DSURFACE_DESC bd;
    D3DVIEWPORT9 vp;
    struct { float x, y, z, rhw, u, v; } quad[4];
    int i;

    if (!cfg.game_gamma || ramp_identity || gamma_failed)
        return;
    if (FAILED(IDirect3DDevice9_GetBackBuffer(dev, 0, 0, D3DBACKBUFFER_TYPE_MONO, &bb)))
        return;
    IDirect3DSurface9_GetDesc(bb, &bd);
    if (!gamma_setup(dev, &bd) || FAILED(IDirect3DTexture9_GetSurfaceLevel(gamma_copy, 0, &copy))) {
        IDirect3DSurface9_Release(bb);
        return;
    }
    if (SUCCEEDED(IDirect3DDevice9_StretchRect(dev, bb, NULL, copy, NULL, D3DTEXF_NONE))) {
        IDirect3DStateBlock9_Capture(gamma_state);
        IDirect3DDevice9_GetRenderTarget(dev, 0, &old_rt);
        IDirect3DDevice9_GetDepthStencilSurface(dev, &old_ds);
        IDirect3DDevice9_SetRenderTarget(dev, 0, bb);
        IDirect3DDevice9_SetDepthStencilSurface(dev, NULL);
        vp.X = vp.Y = 0;
        vp.Width = bd.Width;
        vp.Height = bd.Height;
        vp.MinZ = 0;
        vp.MaxZ = 1;
        IDirect3DDevice9_SetViewport(dev, &vp);
        IDirect3DDevice9_SetRenderState(dev, D3DRS_ZENABLE, D3DZB_FALSE);
        IDirect3DDevice9_SetRenderState(dev, D3DRS_ZWRITEENABLE, FALSE);
        IDirect3DDevice9_SetRenderState(dev, D3DRS_ALPHABLENDENABLE, FALSE);
        IDirect3DDevice9_SetRenderState(dev, D3DRS_ALPHATESTENABLE, FALSE);
        IDirect3DDevice9_SetRenderState(dev, D3DRS_STENCILENABLE, FALSE);
        IDirect3DDevice9_SetRenderState(dev, D3DRS_SCISSORTESTENABLE, FALSE);
        IDirect3DDevice9_SetRenderState(dev, D3DRS_FOGENABLE, FALSE);
        IDirect3DDevice9_SetRenderState(dev, D3DRS_CULLMODE, D3DCULL_NONE);
        IDirect3DDevice9_SetRenderState(dev, D3DRS_COLORWRITEENABLE, 0xF);
        IDirect3DDevice9_SetRenderState(dev, D3DRS_SRGBWRITEENABLE, FALSE);
        IDirect3DDevice9_SetRenderState(dev, D3DRS_FILLMODE, D3DFILL_SOLID);
        IDirect3DDevice9_SetVertexShader(dev, NULL);
        IDirect3DDevice9_SetPixelShader(dev, gamma_ps);
        IDirect3DDevice9_SetFVF(dev, D3DFVF_XYZRHW | D3DFVF_TEX1);
        IDirect3DDevice9_SetTexture(dev, 0, (IDirect3DBaseTexture9 *)gamma_copy);
        IDirect3DDevice9_SetTexture(dev, 1, (IDirect3DBaseTexture9 *)gamma_lut);
        for (i = 0; i < 2; i++) {
            IDirect3DDevice9_SetSamplerState(dev, i, D3DSAMP_MINFILTER, D3DTEXF_POINT);
            IDirect3DDevice9_SetSamplerState(dev, i, D3DSAMP_MAGFILTER, D3DTEXF_POINT);
            IDirect3DDevice9_SetSamplerState(dev, i, D3DSAMP_MIPFILTER, D3DTEXF_NONE);
            IDirect3DDevice9_SetSamplerState(dev, i, D3DSAMP_ADDRESSU, D3DTADDRESS_CLAMP);
            IDirect3DDevice9_SetSamplerState(dev, i, D3DSAMP_ADDRESSV, D3DTADDRESS_CLAMP);
            IDirect3DDevice9_SetSamplerState(dev, i, D3DSAMP_SRGBTEXTURE, FALSE);
        }
        for (i = 0; i < 4; i++) {
            quad[i].x = (i & 1 ? (float)bd.Width : 0.0f) - 0.5f;
            quad[i].y = (i & 2 ? (float)bd.Height : 0.0f) - 0.5f;
            quad[i].z = 0.0f;
            quad[i].rhw = 1.0f;
            quad[i].u = i & 1 ? 1.0f : 0.0f;
            quad[i].v = i & 2 ? 1.0f : 0.0f;
        }
        if (SUCCEEDED(IDirect3DDevice9_BeginScene(dev))) {
            IDirect3DDevice9_DrawPrimitiveUP(dev, D3DPT_TRIANGLESTRIP, 2, quad, sizeof(quad[0]));
            IDirect3DDevice9_EndScene(dev);
        }
        IDirect3DDevice9_SetRenderTarget(dev, 0, old_rt);
        IDirect3DDevice9_SetDepthStencilSurface(dev, old_ds);
        IDirect3DStateBlock9_Apply(gamma_state);
        if (old_rt)
            IDirect3DSurface9_Release(old_rt);
        if (old_ds)
            IDirect3DSurface9_Release(old_ds);
    }
    IDirect3DSurface9_Release(copy);
    IDirect3DSurface9_Release(bb);
}

static IDirect3D9 *d3d;
static UINT d3d_adapter;
static D3DDEVTYPE d3d_type;
static D3DCAPS9 caps;
static HWND game_window;
static int max_aniso;
static DWORD frames, last_report;
static D3DPRESENT_PARAMETERS current_pp;   /* what the device runs with */

static WNDPROC real_wndproc;
static RECT window_rect;     /* where the window is kept */
static DWORD window_style;

static LRESULT CALLBACK Hook_WndProc(HWND w, UINT msg, WPARAM wp, LPARAM lp)
{
    if (msg == WM_WINDOWPOSCHANGING && window_rect.right > window_rect.left) {
        WINDOWPOS *pos = (WINDOWPOS *)lp;
        /* minimising (Alt+Tab away) stays allowed; any other move or resize is pinned */
        if (!IsIconic(w) && pos->x > -30000 && (pos->flags & (SWP_NOMOVE | SWP_NOSIZE)) != (SWP_NOMOVE | SWP_NOSIZE)) {
            pos->x = window_rect.left;
            pos->y = window_rect.top;
            pos->cx = window_rect.right - window_rect.left;
            pos->cy = window_rect.bottom - window_rect.top;
            pos->flags &= ~(SWP_NOMOVE | SWP_NOSIZE);
        }
    } else if (msg == WM_STYLECHANGING && wp == (WPARAM)GWL_STYLE && window_style) {
        ((STYLESTRUCT *)lp)->styleNew = window_style | (((STYLESTRUCT *)lp)->styleNew & (WS_MINIMIZE | WS_VISIBLE | WS_DISABLED));
    }
    return CallWindowProcA(real_wndproc, w, msg, wp, lp);
}

static void place_window(HWND w, UINT bw, UINT bh)
{
    MONITORINFO mi;
    RECT r;
    DWORD style;

    if (!w)
        return;
    mi.cbSize = sizeof(mi);
    if (!GetMonitorInfoA(MonitorFromWindow(w, MONITOR_DEFAULTTOPRIMARY), &mi))
        return;
    if (cfg.mode != MODE_WINDOWED) {
        style = WS_POPUP | WS_VISIBLE;
        r = mi.rcMonitor;
    } else {
        style = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | WS_VISIBLE;
        r.left = r.top = 0;
        r.right = bw;
        r.bottom = bh;
        AdjustWindowRect(&r, style, FALSE);
        OffsetRect(&r, mi.rcWork.left + ((mi.rcWork.right - mi.rcWork.left) - (r.right - r.left)) / 2 - r.left,
                   mi.rcWork.top + ((mi.rcWork.bottom - mi.rcWork.top) - (r.bottom - r.top)) / 2 - r.top);
    }
    window_rect = r;
    window_style = style;
    /* the game resizes its window to the back buffer after device creation: keep ours */
    if (!real_wndproc)
        real_wndproc = (WNDPROC)SetWindowLongPtrA(w, GWLP_WNDPROC, (LONG_PTR)Hook_WndProc);
    SetWindowLongA(w, GWL_STYLE, (LONG)style);
    SetWindowLongA(w, GWL_EXSTYLE, GetWindowLongA(w, GWL_EXSTYLE) & ~(WS_EX_TOPMOST | WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE));
    SetWindowPos(w, HWND_NOTOPMOST, r.left, r.top, r.right - r.left, r.bottom - r.top, SWP_FRAMECHANGED | SWP_SHOWWINDOW);
}

static void adjust_params(D3DPRESENT_PARAMETERS *pp)
{
    DWORD interval = D3DPRESENT_INTERVAL_IMMEDIATE;

    logm("present params in: %ux%u fmt %d windowed %d refresh %u interval %08lx msaa %d/%lu swap %d",
         pp->BackBufferWidth, pp->BackBufferHeight, pp->BackBufferFormat, pp->Windowed, pp->FullScreen_RefreshRateInHz,
         pp->PresentationInterval, pp->MultiSampleType, pp->MultiSampleQuality, pp->SwapEffect);

    if (pp->MultiSampleType != D3DMULTISAMPLE_NONE && d3d &&
        FAILED(IDirect3D9_CheckDeviceMultiSampleType(d3d, d3d_adapter, d3d_type,
                                                     pp->BackBufferFormat == D3DFMT_UNKNOWN ? D3DFMT_X8R8G8B8 : pp->BackBufferFormat,
                                                     TRUE, pp->MultiSampleType, NULL))) {
        logm("MSAA %d not supported - off", pp->MultiSampleType);
        pp->MultiSampleType = D3DMULTISAMPLE_NONE;
        pp->MultiSampleQuality = 0;
    }

    /* always a windowed device: an exclusive one is lost on Alt+Tab and the game cannot reset it */
    pp->Windowed = TRUE;
    pp->FullScreen_RefreshRateInHz = 0;
    if (cfg.vsync)
        interval = D3DPRESENT_INTERVAL_ONE;
    /* fullscreen at 60 Hz with vsync paces itself; everything else goes through the limiter */
    limiter_on = !(cfg.mode == MODE_FULLSCREEN && cfg.vsync && game_hz == 60);
    pp->PresentationInterval = interval;
    logm("present params out: windowed %d refresh %u interval %08lx limiter %s", pp->Windowed,
         pp->FullScreen_RefreshRateInHz, pp->PresentationInterval, limiter_on ? "on" : "off (vsync)");
}

static HRESULT STDMETHODCALLTYPE Hook_SetSamplerState(IDirect3DDevice9 *dev, DWORD s, D3DSAMPLERSTATETYPE type, DWORD value)
{
    if (type == D3DSAMP_MINFILTER && value == D3DTEXF_LINEAR && max_aniso > 1) {
        real_SetSamplerState(dev, s, D3DSAMP_MAXANISOTROPY, (DWORD)max_aniso);
        value = D3DTEXF_ANISOTROPIC;
    }
    return real_SetSamplerState(dev, s, type, value);
}

/* diagnostics (Log=1 only): pick up FPSLimit / FrameIndependent / mouse / pad changes of the ini while playing */
static void debug_reload(void)
{
    static FILETIME last;
    WIN32_FILE_ATTRIBUTE_DATA a;
    static const BYTE fld1[6] = { 0xDD, 0xD8, 0xD9, 0xE8, 0x90, 0x90 };

    if (!GetFileAttributesExA(ini_path, GetFileExInfoStandard, &a) || CompareFileTime(&a.ftLastWriteTime, &last) == 0)
        return;
    if (last.dwLowDateTime || last.dwHighDateTime) {
        load_settings();
        if (!cfg.pad || !cfg.pad_analog) {
            /* the pad no longer walks: give speed and direction back to the keys */
            set_walk_speed(1);
            pad_dir_on = 0;
        }
        if (!cfg.pad) {
            memset(pad_kbd, 0, sizeof(pad_kbd));
            memset(pad_btn, 0, sizeof(pad_btn));
        }
        if (cfg.pad && cfg.pad_analog && !walk_dir_patched)
            patch_walk_direction();
        if (frametime_site)
            write_code(frametime_site, cfg.fix_frametime ? fld1 : frametime_orig, 6);
        next_frame = 0;
        if (cfg.fps > 0 && cfg.fps < 60)
            limiter_on = TRUE; /* test lower frame rates under vsync too */
        logm("ini reloaded: FPSLimit %d, FrameIndependent %d, mouse mode %d, sens %.2f, pad look %.0f",
             cfg.fps, cfg.fix_frametime, cfg.mouse, cfg.sens, cfg.pad_look);
    }
    last = a.ftLastWriteTime;
}

static HRESULT STDMETHODCALLTYPE Hook_Present(IDirect3DDevice9 *dev, const RECT *src, const RECT *dst, HWND wnd, const RGNDATA *dirty)
{
    HRESULT hr;
    DWORD now;

    apply_gamma(dev);
    limit_frame();
    hr = real_Present(dev, src, dst, wnd, dirty);
    update_mouse();
    if (!log_file)
        return hr;
    frame_stats();
    if (!IsBadReadPtr((void *)0x7AB350, 12)) {
        double x = ((float *)0x7AB350)[0], y = ((float *)0x7AB350)[1], d = sqrt((x - walk_last_x) * (x - walk_last_x) + (y - walk_last_y) * (y - walk_last_y));
        if (d > 0.01 && d < 100) {
            walk_dist += d;
            walk_dx += x - walk_last_x;
            walk_dy += y - walk_last_y;
            walk_frames++;
        }
        walk_last_x = x;
        walk_last_y = y;
    }
    track_look();
    frames++;
    now = GetTickCount();
    {
        /* with Log=1 ini changes apply within a quarter second (experiments compare frames side by side) */
        static DWORD last_ini_check;
        if (now - last_ini_check >= 250) {
            last_ini_check = now;
            debug_reload();
        }
    }
    if (now - last_report >= 5000) {
        logm("%.1f fps (longest frame %.1f ms), slider %d, mouse mul %.4g, view turned yaw %.2f deg pitch %.2f deg (%s)",
             frames * 1000.0 / (now - last_report), frame_ms_max, settings_obj ? *(int *)(settings_obj + 0x14) : -1,
             look_mul_yaw, yaw_sum * 57.29578, pitch_sum * 57.29578, in_play() ? "play" : "menu");
        logm("  input: %ld mouse polls, counts x %ld y %ld, %ld key-down samples, %ld failed polls, pad %d, look timer paused %d delta %.4f",
             di_polls, di_counts_x, di_counts_y, di_keys, di_fail, pad_index, timers_obj ? *LOOK_TIMER(0x0C) : -1,
             timers_obj ? *(float *)LOOK_TIMER(0) : -1.0f);
        logm("  walked %.1f units in %d frames (%.2f per frame), heading %.1f deg, view yaw %.1f deg, walk speed %.0f",
             walk_dist, walk_frames, walk_frames ? walk_dist / walk_frames : 0.0, atan2(walk_dy, walk_dx) * 57.29578,
             look_ctrl ? *(float *)(look_ctrl + 0x34) * 57.29578 : 0.0, config_obj ? *(float *)(config_obj + 0x5BC) : 0.0f);
        walk_dist = walk_dx = walk_dy = 0;
        logm("  post-effects active %04lx, skipped %04lx, gamma ramp calls %ld", post_effects_seen,
             post_effects_seen & post_effects_off, gamma_calls);
        post_effects_seen = 0;
        gamma_calls = 0;
        walk_frames = 0;
        di_polls = di_counts_x = di_counts_y = di_keys = di_fail = 0;
        frames = 0;
        frame_ms_max = 0;
        yaw_sum = pitch_sum = 0;
        last_report = now;
    }
    return hr;
}

static HRESULT STDMETHODCALLTYPE Hook_Reset(IDirect3DDevice9 *dev, D3DPRESENT_PARAMETERS *pp)
{
    HRESULT hr;

    adjust_params(pp);
    /*
     * The game resets on every focus change, without releasing its D3DPOOL_DEFAULT resources, so
     * a real Reset fails (D3DERR_INVALIDCALL) and the game crashes - the original Alt+Tab crash.
     * A windowed device is not lost by focus changes: when nothing changes, there is nothing to reset.
     */
    if (IDirect3DDevice9_TestCooperativeLevel(dev) == D3D_OK && pp->BackBufferWidth == current_pp.BackBufferWidth &&
        pp->BackBufferHeight == current_pp.BackBufferHeight && pp->BackBufferFormat == current_pp.BackBufferFormat &&
        pp->MultiSampleType == current_pp.MultiSampleType && pp->Windowed == current_pp.Windowed &&
        pp->PresentationInterval == current_pp.PresentationInterval) {
        logm("Reset skipped (device not lost, nothing changed)");
        place_window(game_window, pp->BackBufferWidth, pp->BackBufferHeight);
        next_frame = 0;
        return D3D_OK;
    }
    gamma_release_default();
    hr = real_Reset(dev, pp);
    logm("Reset -> %08lx", (DWORD)hr);
    if (SUCCEEDED(hr)) {
        current_pp = *pp;
        place_window(game_window, pp->BackBufferWidth, pp->BackBufferHeight);
    }
    next_frame = 0;
    return hr;
}

static HRESULT STDMETHODCALLTYPE Hook_CreateDevice(IDirect3D9 *self_, UINT adapter, D3DDEVTYPE type, HWND focus, DWORD flags,
                                                   D3DPRESENT_PARAMETERS *pp, IDirect3DDevice9 **out)
{
    HRESULT hr;

    d3d_adapter = adapter;
    d3d_type = type;
    IDirect3D9_GetDeviceCaps(self_, adapter, type, &caps);
    max_aniso = cfg.anisotropy > (int)caps.MaxAnisotropy ? (int)caps.MaxAnisotropy : cfg.anisotropy;
    game_window = pp->hDeviceWindow ? pp->hDeviceWindow : focus;
    set_display_refresh(TRUE);
    adjust_params(pp);
    hr = real_CreateDevice(self_, adapter, type, focus, flags, pp, out);
    logm("CreateDevice adapter %u flags %08lx -> %08lx, anisotropy %d", adapter, flags, (DWORD)hr, max_aniso);
    if (FAILED(hr))
        return hr;
    current_pp = *pp;
    place_window(game_window, pp->BackBufferWidth, pp->BackBufferHeight);
    if (!real_Present) {
        real_Present = (Present_t)patch_vtable(*out, 17, Hook_Present);
        real_Reset = (Reset_t)patch_vtable(*out, 16, Hook_Reset);
        if (max_aniso > 1)
            real_SetSamplerState = (SetSamplerState_t)patch_vtable(*out, 69, Hook_SetSamplerState);
        if (cfg.game_gamma || log_file)
            real_SetGammaRamp = (SetGammaRamp_t)patch_vtable(*out, 21, Hook_SetGammaRamp);
    }
    return hr;
}

static IDirect3D9 *WINAPI Hook_Direct3DCreate9(UINT sdk)
{
    IDirect3D9 *r = real_Direct3DCreate9(sdk);

    if (r && !real_CreateDevice)
        real_CreateDevice = (CreateDevice_t)patch_vtable(r, 16, Hook_CreateDevice);
    d3d = r;
    return r;
}

/* ---- DirectInput: the gamepad joins the game's own keyboard and mouse polls ---------------- */

typedef HRESULT (WINAPI *DirectInput8Create_t)(HINSTANCE, DWORD, REFIID, void **, void *);
typedef HRESULT (STDMETHODCALLTYPE *DICreateDevice_t)(void *, REFGUID, void **, void *);
typedef HRESULT (STDMETHODCALLTYPE *DIAcquire_t)(void *);
typedef HRESULT (STDMETHODCALLTYPE *DIGetDeviceState_t)(void *, DWORD, void *);
static DirectInput8Create_t real_DirectInput8Create;
static DICreateDevice_t real_DICreateDevice;
static DIAcquire_t real_DIAcquire;
static DIGetDeviceState_t real_DIGetDeviceState;

static HRESULT STDMETHODCALLTYPE Hook_DIAcquire(void *dev)
{
    HRESULT hr = real_DIAcquire(dev);
    static int logged;
    if (FAILED(hr) && logged++ < 20)
        logm("DirectInput Acquire -> %08lx", (DWORD)hr);
    return hr;
}

static HRESULT STDMETHODCALLTYPE Hook_DIGetDeviceState(void *dev, DWORD size, void *data)
{
    HRESULT hr = real_DIGetDeviceState(dev, size, data);

    /* a failed poll means the game is not in front: no pad input either */
    if (FAILED(hr)) {
        di_fail++;
        return hr;
    }
    if (size == sizeof(DIMOUSESTATE) || size == 20 /* DIMOUSESTATE2 */) {
        if (cfg.pad) {
            pad_read();
            pad_mouse((DIMOUSESTATE *)data);
        }
        di_polls++;
        di_counts_x += ((LONG *)data)[0];
        di_counts_y += ((LONG *)data)[1];
    } else if (size == 256) {
        int i;
        if (cfg.pad) {
            pad_read();
            for (i = 0; i < 256; i++)
                ((BYTE *)data)[i] |= pad_kbd[i];
        }
        for (i = 0; i < 256; i++)
            if (((BYTE *)data)[i] & 0x80)
                di_keys++;
    }
    return hr;
}

static HRESULT STDMETHODCALLTYPE Hook_DICreateDevice(void *self_, REFGUID guid, void **dev, void *outer)
{
    HRESULT hr = real_DICreateDevice(self_, guid, dev, outer);
    /* keyboard and mouse share one device vtable: hooking it once covers both */
    if (SUCCEEDED(hr) && !real_DIGetDeviceState) {
        real_DIAcquire = (DIAcquire_t)patch_vtable(*dev, 7, Hook_DIAcquire);
        real_DIGetDeviceState = (DIGetDeviceState_t)patch_vtable(*dev, 9, Hook_DIGetDeviceState);
    }
    return hr;
}

static HRESULT WINAPI Hook_DirectInput8Create(HINSTANCE inst, DWORD ver, REFIID iid, void **out, void *outer)
{
    HRESULT hr = real_DirectInput8Create(inst, ver, iid, out, outer);
    if (SUCCEEDED(hr) && !real_DICreateDevice)
        real_DICreateDevice = (DICreateDevice_t)patch_vtable(*out, 3, Hook_DICreateDevice);
    return hr;
}

/* ---- FMOD Ex (diagnostics only: the game's audio setup is left exactly as shipped) ---------- */

typedef int (WINAPI *FMOD_System_Init_t)(void *, int, unsigned, void *);
typedef int (WINAPI *FMOD_Get1_t)(void *, int *);
typedef int (WINAPI *FMOD_GetSoftwareFormat_t)(void *, int *, int *, int *, int *, int *, int *);
typedef int (WINAPI *FMOD_GetHardwareChannels_t)(void *, int *, int *, int *);
typedef int (WINAPI *FMOD_CreateSound_t)(void *, const char *, unsigned, void *, void **);
static FMOD_System_Init_t real_FMOD_System_Init;
static FMOD_CreateSound_t real_FMOD_CreateSound;
static unsigned sound_modes_seen;

static int WINAPI Hook_FMOD_System_Init(void *sys, int channels, unsigned flags, void *extra)
{
    int r = real_FMOD_System_Init(sys, channels, flags, extra);
    HMODULE fm = GetModuleHandleA("fmodex.dll");
    FMOD_Get1_t speaker = fm ? (FMOD_Get1_t)GetProcAddress(fm, "FMOD_System_GetSpeakerMode") : NULL;
    FMOD_Get1_t output = fm ? (FMOD_Get1_t)GetProcAddress(fm, "FMOD_System_GetOutput") : NULL;
    FMOD_GetSoftwareFormat_t fmt = fm ? (FMOD_GetSoftwareFormat_t)GetProcAddress(fm, "FMOD_System_GetSoftwareFormat") : NULL;
    FMOD_GetHardwareChannels_t hw = fm ? (FMOD_GetHardwareChannels_t)GetProcAddress(fm, "FMOD_System_GetHardwareChannels") : NULL;
    int sm = -1, out = -1, rate = 0, f = 0, och = 0, ich = 0, res = 0, bits = 0, h2 = 0, h3 = 0, ht = 0;

    if (speaker) speaker(sys, &sm);
    if (output) output(sys, &out);
    if (fmt) fmt(sys, &rate, &f, &och, &ich, &res, &bits);
    if (hw) hw(sys, &h2, &h3, &ht);
    logm("FMOD init(%d channels, flags %x) -> %d: output %d, speaker mode %d, %d Hz, %d out channels, hw voices 2d %d 3d %d",
         channels, flags, r, out, sm, rate, och, h2, h3);
    return r;
}

static int WINAPI Hook_FMOD_CreateSound(void *sys, const char *name, unsigned mode, void *info, void **sound)
{
    if ((mode & ~sound_modes_seen) && log_file) {
        sound_modes_seen |= mode;
        logm("FMOD sound mode %08x (%s)", mode, name && !(mode & 0x800) ? name : "-");
    }
    return real_FMOD_CreateSound(sys, name, mode, info, sound);
}

/* ---- entry --------------------------------------------------------------------------------- */

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID reserved)
{
    if (reason == DLL_PROCESS_ATTACH) {
        typedef HANDLE (WINAPI *CreateWaitableTimerExW_t)(LPSECURITY_ATTRIBUTES, LPCWSTR, DWORD, DWORD);
        CreateWaitableTimerExW_t cwt;

        self = inst;
        DisableThreadLibraryCalls(inst);
        {
            /* the game's code and COM objects point into this DLL from now on: never unload it */
            HMODULE pinned;
            GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_PIN | GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS, (LPCSTR)DllMain, &pinned);
        }
        load_settings();
        if (cfg.log)
            log_file = fopen(log_path, "w");
        logm("CoCHD.asi 1.2.2 - %s", ini_path);
        SetProcessDPIAware();
        timeBeginPeriod(1);
        QueryPerformanceFrequency(&qpc_freq);
        cwt = (CreateWaitableTimerExW_t)GetProcAddress(GetModuleHandleA("kernel32.dll"), "CreateWaitableTimerExW");
        if (cwt)
            wait_timer = cwt(NULL, NULL, 0x2 /* CREATE_WAITABLE_TIMER_HIGH_RESOLUTION */, TIMER_ALL_ACCESS);
        if (!wait_timer)
            wait_timer = CreateWaitableTimerA(NULL, TRUE, NULL);

        choose_mode();
        real_SHGetValueA = (SHGetValueA_t)patch_iat("SHLWAPI.dll", "SHGetValueA", Hook_SHGetValueA);
        real_Direct3DCreate9 = (Direct3DCreate9_t)patch_iat("d3d9.dll", "Direct3DCreate9", Hook_Direct3DCreate9);
        if (cfg.pad) {
            HMODULE x = LoadLibraryA("xinput1_4.dll");
            if (!x) x = LoadLibraryA("xinput1_3.dll");
            if (!x) x = LoadLibraryA("xinput9_1_0.dll");
            xinput_get = x ? (XInputGetState_t)GetProcAddress(x, "XInputGetState") : NULL;
            {
                HANDLE t;
                InitializeCriticalSection(&sony_lock);
                t = CreateThread(NULL, 0, sony_watcher, NULL, 0, NULL);
                if (t)
                    CloseHandle(t);
            }
        }
        if (cfg.log || cfg.pad)
            real_DirectInput8Create = (DirectInput8Create_t)patch_iat("DINPUT8.dll", "DirectInput8Create", Hook_DirectInput8Create);
        if (cfg.log) {
            real_FMOD_System_Init = (FMOD_System_Init_t)patch_iat("fmodex.dll", "FMOD_System_Init", Hook_FMOD_System_Init);
            real_FMOD_CreateSound = (FMOD_CreateSound_t)patch_iat("fmodex.dll", "FMOD_System_CreateSound", Hook_FMOD_CreateSound);
        }
        logm("hooks: SHGetValueA %p, Direct3DCreate9 %p, DirectInput8Create %p, XInput %p", (void *)real_SHGetValueA,
             (void *)real_Direct3DCreate9, (void *)real_DirectInput8Create, (void *)xinput_get);
        patch_mouse();
        if (cfg.pad && cfg.pad_analog)
            patch_walk_direction();
        logm("walk direction %s", walk_dir_patched ? "analog" : "8-way");
        patch_film_grain();
        if (cfg.log)
            hook_post_effects();
        logm("film grain %s (%s)", grain_on ? "on" : "off", grain_patched ? "switchable" : "draw call not found");
    } else if (reason == DLL_PROCESS_DETACH) {
        set_display_refresh(FALSE);
        set_walk_speed(1);
        if (log_file)
            fclose(log_file);
    }
    return TRUE;
}
