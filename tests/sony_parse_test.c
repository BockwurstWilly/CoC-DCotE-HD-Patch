/* Offline test of the PlayStation report parser (src/asi/sony_pad.h): crafted reports in the documented layouts.
   Build + run: tests\sony_parse_test.ps1 */
#include <windows.h>
#include <xinput.h>
#include <stdio.h>
#include <string.h>
#include "../src/asi/sony_pad.h"

static int fails;

static void check(const char *name, const BYTE *r, DWORD len, BOOL ds, WORD buttons, SHORT lx, SHORT ly, SHORT rx, SHORT ry, BYTE lt, BYTE rt)
{
    XINPUT_GAMEPAD g;
    memset(&g, 0xCC, sizeof(g));
    if (!sony_parse(r, len, ds, &g)) {
        printf("FAIL  %s: not parsed\n", name);
        fails++;
        return;
    }
    if (g.wButtons != buttons || g.sThumbLX != lx || g.sThumbLY != ly || g.sThumbRX != rx || g.sThumbRY != ry ||
        g.bLeftTrigger != lt || g.bRightTrigger != rt) {
        printf("FAIL  %s: buttons %04x lx %d ly %d rx %d ry %d lt %d rt %d\n", name, g.wButtons, g.sThumbLX, g.sThumbLY,
               g.sThumbRX, g.sThumbRY, g.bLeftTrigger, g.bRightTrigger);
        fails++;
    } else
        printf("PASS  %s\n", name);
}

int main(void)
{
    BYTE r[80];
    /* sticks: left full left + full up, right full right + centre (128 -> y -129); cross + d-pad right; L1 + options */
    const WORD want = XINPUT_GAMEPAD_A | XINPUT_GAMEPAD_DPAD_RIGHT | XINPUT_GAMEPAD_LEFT_SHOULDER | XINPUT_GAMEPAD_START;
    BOOL ds;
    XINPUT_GAMEPAD g;

    /* DualShock 4 USB */
    memset(r, 0, sizeof(r));
    r[0] = 0x01; r[1] = 0; r[2] = 0; r[3] = 255; r[4] = 128; r[5] = 0x20 | 2; r[6] = 0x01 | 0x20; r[8] = 200; r[9] = 10;
    check("DS4 USB", r, 64, FALSE, want, -32768, 32767, 32767, -129, 200, 10);
    check("DS4 Bluetooth basic", r, 10, FALSE, want, -32768, 32767, 32767, -129, 200, 10);
    /* DualShock 4 Bluetooth full: same data two bytes later */
    memset(r, 0, sizeof(r));
    r[0] = 0x11; r[3] = 0; r[4] = 0; r[5] = 255; r[6] = 128; r[7] = 0x20 | 2; r[8] = 0x21; r[10] = 200; r[11] = 10;
    check("DS4 Bluetooth full", r, 78, FALSE, want, -32768, 32767, 32767, -129, 200, 10);
    /* DualSense USB */
    memset(r, 0, sizeof(r));
    r[0] = 0x01; r[1] = 0; r[2] = 0; r[3] = 255; r[4] = 128; r[5] = 200; r[6] = 10; r[8] = 0x20 | 2; r[9] = 0x21;
    check("DualSense USB", r, 64, TRUE, want, -32768, 32767, 32767, -129, 200, 10);
    /* DualSense Bluetooth full */
    memset(r, 0, sizeof(r));
    r[0] = 0x31; r[2] = 0; r[3] = 0; r[4] = 255; r[5] = 128; r[6] = 200; r[7] = 10; r[9] = 0x20 | 2; r[10] = 0x21;
    check("DualSense Bluetooth full", r, 78, TRUE, want, -32768, 32767, 32767, -129, 200, 10);
    /* DualSense Bluetooth basic: DS4 layout */
    memset(r, 0, sizeof(r));
    r[0] = 0x01; r[3] = 255; r[4] = 128; r[5] = 0x20 | 2; r[6] = 0x21; r[8] = 200; r[9] = 10;
    check("DualSense Bluetooth basic", r, 10, TRUE, want, -32768, 32767, 32767, -129, 200, 10);
    /* centred sticks, released hat, other face buttons and the rest of the shoulder row */
    memset(r, 0, sizeof(r));
    r[0] = 0x01; r[1] = r[2] = r[3] = r[4] = 128; r[5] = 0x08 | 0x10 | 0x40 | 0x80; r[6] = 0x02 | 0x10 | 0x40 | 0x80;
    check("DS4 other buttons", r, 64, FALSE,
          XINPUT_GAMEPAD_X | XINPUT_GAMEPAD_B | XINPUT_GAMEPAD_Y | XINPUT_GAMEPAD_RIGHT_SHOULDER | XINPUT_GAMEPAD_BACK |
          XINPUT_GAMEPAD_LEFT_THUMB | XINPUT_GAMEPAD_RIGHT_THUMB, 128, -129, 128, -129, 0, 0);
    /* every hat direction */
    {
        static const WORD hat[8] = { XINPUT_GAMEPAD_DPAD_UP, XINPUT_GAMEPAD_DPAD_UP | XINPUT_GAMEPAD_DPAD_RIGHT,
            XINPUT_GAMEPAD_DPAD_RIGHT, XINPUT_GAMEPAD_DPAD_DOWN | XINPUT_GAMEPAD_DPAD_RIGHT, XINPUT_GAMEPAD_DPAD_DOWN,
            XINPUT_GAMEPAD_DPAD_DOWN | XINPUT_GAMEPAD_DPAD_LEFT, XINPUT_GAMEPAD_DPAD_LEFT,
            XINPUT_GAMEPAD_DPAD_UP | XINPUT_GAMEPAD_DPAD_LEFT };
        int h, bad = 0;
        for (h = 0; h < 8; h++) {
            memset(r, 128, sizeof(r));
            r[0] = 0x01; r[5] = (BYTE)h; r[6] = 0; r[8] = r[9] = 0;
            if (!sony_parse(r, 64, FALSE, &g) || g.wButtons != hat[h])
                bad++;
        }
        printf("%s  hat directions\n", bad ? "FAIL" : "PASS");
        fails += bad;
    }
    /* reports without pad data */
    memset(r, 0, sizeof(r));
    r[0] = 0x05;
    if (sony_parse(r, 64, FALSE, &g) || sony_parse(r, 4, FALSE, &g)) {
        printf("FAIL  foreign report accepted\n");
        fails++;
    } else
        printf("PASS  foreign/short reports ignored\n");
    printf("pid table: 05C4 %d 09CC %d 0CE6 %d 0DF2 %d 1234 %d\n", sony_pid_known(0x05C4, &ds), sony_pid_known(0x09CC, &ds),
           sony_pid_known(0x0CE6, &ds), sony_pid_known(0x0DF2, &ds), sony_pid_known(0x1234, &ds));
    printf(fails ? "== %d FAILED\n" : "== ALL PASSED\n", fails);
    return fails != 0;
}
