/* Test stand-in for xinput1_4.dll: pad 0 reports the XINPUT_STATE in the shared memory
   "Local\CoCHD_FakePad" (written by the test script); no mapping = no pad. */
#include <windows.h>
#include <xinput.h>

DWORD WINAPI XInputGetState(DWORD index, XINPUT_STATE *state)
{
    HANDLE m;
    void *p;

    if (index != 0)
        return ERROR_DEVICE_NOT_CONNECTED;
    m = OpenFileMappingA(FILE_MAP_READ, FALSE, "Local\\CoCHD_FakePad");
    if (!m)
        return ERROR_DEVICE_NOT_CONNECTED;
    p = MapViewOfFile(m, FILE_MAP_READ, 0, 0, sizeof(XINPUT_STATE));
    if (p) {
        memcpy(state, p, sizeof(XINPUT_STATE));
        UnmapViewOfFile(p);
    }
    CloseHandle(m);
    return p ? ERROR_SUCCESS : ERROR_DEVICE_NOT_CONNECTED;
}
