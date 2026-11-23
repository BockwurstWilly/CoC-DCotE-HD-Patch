/*
 * PlayStation controllers (DualShock 4, DualSense) read directly as HID devices and turned into an XInput state,
 * so everything the patch does with an Xbox pad works with them too - no Steam Input or DS4Windows needed.
 *
 * Input reports (byte 0 = report id):
 *   DualShock 4  USB 0x01 (64 bytes) and Bluetooth "basic" 0x01 (10 bytes): data from byte 1
 *                Bluetooth "full" 0x11 (78 bytes): data from byte 3
 *     +0 LX +1 LY +2 RX +3 RY  +4 hat (low nibble) | square 10 cross 20 circle 40 triangle 80
 *     +5 L1 01 R1 02 L2 04 R2 08 share 10 options 20 L3 40 R3 80   +6 PS 01 touchpad 02   +7 L2 +8 R2
 *   DualSense    USB 0x01 (64 bytes): data from byte 1;  Bluetooth "full" 0x31 (78 bytes): data from byte 2
 *     +0 LX +1 LY +2 RX +3 RY  +4 L2 +5 R2  +6 counter  +7 hat | face buttons as above  +8 as DS4 +5  +9 PS 01
 *                Bluetooth "basic" 0x01 (10 bytes): laid out like the DualShock 4
 * Sticks: 0..255, y grows downwards. Hat: 0 up, clockwise to 7 up-left, 8 = released.
 */
#ifndef SONY_PAD_H
#define SONY_PAD_H

#define SONY_VID 0x054C

/* product ids: DualShock 4 (v1, v2, wireless adapter), DualSense, DualSense Edge */
static BOOL sony_pid_known(USHORT pid, BOOL *dualsense)
{
    switch (pid) {
    case 0x05C4: case 0x09CC: case 0x0BA0:
        *dualsense = FALSE;
        return TRUE;
    case 0x0CE6: case 0x0DF2:
        *dualsense = TRUE;
        return TRUE;
    }
    return FALSE;
}

static SHORT sony_axis(BYTE v, BOOL invert)
{
    int x = (int)v * 257 - 32768; /* 0 -> -32768, 255 -> 32767 */
    if (invert)
        x = -1 - x;               /* -32768 <-> 32767 */
    return (SHORT)x;
}

/* turns one input report into an XInput gamepad state; FALSE for reports that carry no pad data */
static BOOL sony_parse(const BYTE *r, DWORD len, BOOL dualsense, XINPUT_GAMEPAD *g)
{
    static const WORD hat_bits[8] = {
        XINPUT_GAMEPAD_DPAD_UP, XINPUT_GAMEPAD_DPAD_UP | XINPUT_GAMEPAD_DPAD_RIGHT, XINPUT_GAMEPAD_DPAD_RIGHT,
        XINPUT_GAMEPAD_DPAD_DOWN | XINPUT_GAMEPAD_DPAD_RIGHT, XINPUT_GAMEPAD_DPAD_DOWN,
        XINPUT_GAMEPAD_DPAD_DOWN | XINPUT_GAMEPAD_DPAD_LEFT, XINPUT_GAMEPAD_DPAD_LEFT,
        XINPUT_GAMEPAD_DPAD_UP | XINPUT_GAMEPAD_DPAD_LEFT };
    const BYTE *d;
    BYTE b1, b2, lt, rt;
    WORD w = 0;
    BOOL ds_layout;

    if (len < 10)
        return FALSE;
    if (r[0] == 0x01 && (len >= 64 || !dualsense)) {
        d = r + 1;
        ds_layout = dualsense;           /* USB (both) and DS4 Bluetooth basic */
    } else if (r[0] == 0x01) {
        d = r + 1;
        ds_layout = FALSE;               /* DualSense Bluetooth basic report: DS4 layout */
    } else if (r[0] == 0x11 && !dualsense && len >= 13) {
        d = r + 3;
        ds_layout = FALSE;
    } else if (r[0] == 0x31 && dualsense && len >= 12) {
        d = r + 2;
        ds_layout = TRUE;
    } else
        return FALSE;
    if (ds_layout) {
        if (d + 9 > r + len)
            return FALSE;
        b1 = d[7]; b2 = d[8]; lt = d[4]; rt = d[5];
    } else {
        if (d + 9 > r + len)
            return FALSE;
        b1 = d[4]; b2 = d[5]; lt = d[7]; rt = d[8];
    }
    if ((b1 & 0x0F) < 8)
        w |= hat_bits[b1 & 0x0F];
    if (b1 & 0x10) w |= XINPUT_GAMEPAD_X;            /* square */
    if (b1 & 0x20) w |= XINPUT_GAMEPAD_A;            /* cross */
    if (b1 & 0x40) w |= XINPUT_GAMEPAD_B;            /* circle */
    if (b1 & 0x80) w |= XINPUT_GAMEPAD_Y;            /* triangle */
    if (b2 & 0x01) w |= XINPUT_GAMEPAD_LEFT_SHOULDER;
    if (b2 & 0x02) w |= XINPUT_GAMEPAD_RIGHT_SHOULDER;
    if (b2 & 0x10) w |= XINPUT_GAMEPAD_BACK;         /* share / create */
    if (b2 & 0x20) w |= XINPUT_GAMEPAD_START;        /* options */
    if (b2 & 0x40) w |= XINPUT_GAMEPAD_LEFT_THUMB;
    if (b2 & 0x80) w |= XINPUT_GAMEPAD_RIGHT_THUMB;
    g->wButtons = w;
    g->bLeftTrigger = lt;
    g->bRightTrigger = rt;
    g->sThumbLX = sony_axis(d[0], FALSE);
    g->sThumbLY = sony_axis(d[1], TRUE);
    g->sThumbRX = sony_axis(d[2], FALSE);
    g->sThumbRY = sony_axis(d[3], TRUE);
    return TRUE;
}

#endif
