#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <xinput.h>
#include <string.h>

/* SDL snapshots and rumble commands keep two Cemu ports independent of device APIs. */
typedef struct {
    LONG enabled, player[2]; DWORD heartbeat;
    XINPUT_STATE states[4]; DWORD vibration[4]; LONG rumbleSequence[4], sequence;
} Routing;
static HMODULE real;
static HANDLE mapping;
static volatile Routing *routing;
static INIT_ONCE once = INIT_ONCE_STATIC_INIT;

static BOOL CALLBACK initialize(PINIT_ONCE o, PVOID p, PVOID *c) {
    WCHAR path[MAX_PATH];
    GetSystemDirectoryW(path, MAX_PATH);
    lstrcatW(path, L"\\xinput1_4.dll");
    real = LoadLibraryW(path);
    return TRUE;
}
static FARPROC proc(const char *name) {
#ifdef BOTW_BRIDGE_TEST
    extern FARPROC mock_proc(const char *name);
    return mock_proc(name);
#else
    InitOnceExecuteOnce(&once, initialize, NULL, NULL);
    return real ? GetProcAddress(real, name) : NULL;
#endif
}
static BOOL active(void) {
    if (!routing) {
        mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, L"Local\\HyruleCoop.Routing.v2");
        if (mapping) routing = (Routing*)MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(Routing));
    }
    return routing && routing->enabled == 1 && (DWORD)(GetTickCount() - routing->heartbeat) < 5000;
}
static DWORD route(DWORD index) {
    if (index > 1 || routing->player[index] < 0 || routing->player[index] > 3) return 4;
    return (DWORD)routing->player[index];
}
__declspec(dllexport) DWORD WINAPI XInputGetState(DWORD index, XINPUT_STATE *state) {
    typedef DWORD (WINAPI *Fn)(DWORD, XINPUT_STATE*);
    Fn fn = (Fn)proc("XInputGetState");
    if (!state) return ERROR_BAD_ARGUMENTS;
    if (!active()) return fn ? fn(index,state) : ERROR_DEVICE_NOT_CONNECTED;
    ZeroMemory(state,sizeof(*state));
    if (index > 1) return ERROR_DEVICE_NOT_CONNECTED;
    for (int attempt=0;attempt<3;attempt++) {
        LONG version=routing->sequence;
        if(version & 1) continue;
        MemoryBarrier();
        DWORD physical=route(index);
        XINPUT_STATE snapshot={0};
        if(physical<4) memcpy(&snapshot,(const void*)&routing->states[physical],sizeof(snapshot));
        MemoryBarrier();
        if(version==routing->sequence) { *state=snapshot; return ERROR_SUCCESS; }
    }
    return ERROR_SUCCESS;
}
__declspec(dllexport) DWORD WINAPI XInputSetState(DWORD index, XINPUT_VIBRATION *vibration) {
    typedef DWORD (WINAPI *Fn)(DWORD,XINPUT_VIBRATION*);
    Fn fn = (Fn)proc("XInputSetState");
    if (!vibration) return ERROR_BAD_ARGUMENTS;
    if (!active()) return fn ? fn(index,vibration) : ERROR_DEVICE_NOT_CONNECTED;
    if (index > 1) return ERROR_DEVICE_NOT_CONNECTED;
    DWORD physical = route(index);
    if(physical<4) {
        InterlockedExchange((volatile LONG*)&routing->vibration[physical],(LONG)((DWORD)vibration->wLeftMotorSpeed | ((DWORD)vibration->wRightMotorSpeed << 16)));
        InterlockedIncrement(&routing->rumbleSequence[physical]);
    }
    return ERROR_SUCCESS;
}
__declspec(dllexport) DWORD WINAPI XInputGetCapabilities(DWORD index,DWORD flags,XINPUT_CAPABILITIES *caps) {
    typedef DWORD (WINAPI *Fn)(DWORD,DWORD,XINPUT_CAPABILITIES*);
    Fn fn = (Fn)proc("XInputGetCapabilities");
    if (!caps) return ERROR_BAD_ARGUMENTS;
    if (!active()) return fn ? fn(index,flags,caps) : ERROR_DEVICE_NOT_CONNECTED;
    ZeroMemory(caps,sizeof(*caps));
    if(index > 1) return ERROR_DEVICE_NOT_CONNECTED;
    caps->Type = XINPUT_DEVTYPE_GAMEPAD;
    caps->SubType = XINPUT_DEVSUBTYPE_GAMEPAD;
    caps->Flags = XINPUT_CAPS_FFB_SUPPORTED;
    caps->Gamepad.wButtons = 0xf3ff;
    caps->Gamepad.bLeftTrigger = caps->Gamepad.bRightTrigger = 255;
    caps->Gamepad.sThumbLX = caps->Gamepad.sThumbLY = caps->Gamepad.sThumbRX = caps->Gamepad.sThumbRY = 32767;
    caps->Vibration.wLeftMotorSpeed = caps->Vibration.wRightMotorSpeed = 65535;
    return ERROR_SUCCESS;
}
__declspec(dllexport) void WINAPI XInputEnable(BOOL enable) {
    typedef void (WINAPI *Fn)(BOOL);
    Fn fn=(Fn)proc("XInputEnable"); if(!active() && fn) fn(enable);
}
__declspec(dllexport) DWORD WINAPI XInputGetBatteryInformation(DWORD index,BYTE type,XINPUT_BATTERY_INFORMATION *battery) {
    typedef DWORD (WINAPI *Fn)(DWORD,BYTE,XINPUT_BATTERY_INFORMATION*);
    Fn fn=(Fn)proc("XInputGetBatteryInformation");
    if(!battery) return ERROR_BAD_ARGUMENTS;
    ZeroMemory(battery,sizeof(*battery));
    if(active()) {
        if(index>1 || route(index)>3) return ERROR_DEVICE_NOT_CONNECTED;
        battery->BatteryType=BATTERY_TYPE_WIRED;
        battery->BatteryLevel=BATTERY_LEVEL_FULL;
        return ERROR_SUCCESS;
    }
    return index<4 && fn ? fn(index,type,battery) : ERROR_DEVICE_NOT_CONNECTED;
}
__declspec(dllexport) DWORD WINAPI XInputGetKeystroke(DWORD index,DWORD reserved,PXINPUT_KEYSTROKE key) {
    typedef DWORD (WINAPI *Fn)(DWORD,DWORD,PXINPUT_KEYSTROKE);
    Fn fn=(Fn)proc("XInputGetKeystroke");
    if(!key) return ERROR_BAD_ARGUMENTS;
    if(active()) return index<2 ? ERROR_EMPTY : ERROR_DEVICE_NOT_CONNECTED;
    return index<4 && fn ? fn(index,reserved,key) : ERROR_DEVICE_NOT_CONNECTED;
}
__declspec(dllexport) DWORD WINAPI XInputGetAudioDeviceIds(DWORD index,LPWSTR render,UINT *rc,LPWSTR capture,UINT *cc) {
    typedef DWORD (WINAPI *Fn)(DWORD,LPWSTR,UINT*,LPWSTR,UINT*);
    Fn fn=(Fn)proc("XInputGetAudioDeviceIds");
    if(active()) return ERROR_DEVICE_NOT_CONNECTED;
    return index<4 && fn ? fn(index,render,rc,capture,cc) : ERROR_DEVICE_NOT_CONNECTED;
}
BOOL WINAPI DllMain(HINSTANCE h,DWORD reason,LPVOID reserved) { return TRUE; }
