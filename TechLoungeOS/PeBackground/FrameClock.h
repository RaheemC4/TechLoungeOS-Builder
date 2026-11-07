#ifndef TLOS_FRAME_CLOCK_H
#define TLOS_FRAME_CLOCK_H
#include <windows.h>

typedef struct {HANDLE timer;double next,interval;unsigned refresh;} FrameClock;
static unsigned displayRefresh(HWND window){
    MONITORINFOEXW monitor={0};monitor.cbSize=sizeof(monitor);DEVMODEW mode={0};mode.dmSize=sizeof(mode);
    if(!GetMonitorInfoW(MonitorFromWindow(window,MONITOR_DEFAULTTONEAREST),(MONITORINFO*)&monitor)||!EnumDisplaySettingsW(monitor.szDevice,ENUM_CURRENT_SETTINGS,&mode))return 60;
    unsigned refresh=mode.dmDisplayFrequency;if(refresh<30||refresh>240)return 60;return refresh>120?120:refresh;
}
static void stopClock(FrameClock *clock){if(clock->timer){CancelWaitableTimer(clock->timer);CloseHandle(clock->timer);clock->timer=NULL;}}
static int armClock(FrameClock *clock,double time){
    if(clock->next<=time)clock->next=time+clock->interval;
    LONGLONG ticks=(LONGLONG)((clock->next-time)*10000000.0);if(ticks<1)ticks=1;
    LARGE_INTEGER due;due.QuadPart=-ticks;
    if(!SetWaitableTimer(clock->timer,&due,0,NULL,NULL,FALSE)){stopClock(clock);return 0;}return 1;
}
static int startClock(FrameClock *clock,HWND window,double time){
    // Optional high-resolution timer; unavailable kernels retain the WM_TIMER path.
    clock->timer=CreateWaitableTimerExW(NULL,NULL,0x00000002,TIMER_MODIFY_STATE|SYNCHRONIZE);
    if(!clock->timer)return 0;
    clock->refresh=displayRefresh(window);clock->interval=1.0/clock->refresh;clock->next=time+clock->interval;
    return armClock(clock,time);
}
#endif
