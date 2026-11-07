// Original native WinPE ambient background. No network, disk access or extra runtimes.
#ifndef UNICODE
#define UNICODE
#endif
#define _UNICODE
#include <windows.h>
#include <math.h>
#include <stdint.h>
#include "Ambient.h"
static uint32_t pixels[RW*RH];
static BITMAPINFO bitmap;
static int moving=1, highContrast=0, staticMode=0;
static ULONGLONG started;
static HWND window;
static void frame(double t){RenderFrame(pixels,t,highContrast);}
static void appearance(void) {
    BOOL enabled=TRUE;HIGHCONTRAST hc={0};hc.cbSize=sizeof(hc);
    SystemParametersInfoW(SPI_GETHIGHCONTRAST,sizeof(hc),&hc,0);
    highContrast=(hc.dwFlags&HCF_HIGHCONTRASTON)!=0;
    SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION,0,&enabled,0);
    moving=enabled&&!highContrast&&!staticMode;
}
static LRESULT CALLBACK proc(HWND h,UINT msg,WPARAM wp,LPARAM lp) {
    switch(msg) {
    case WM_CREATE:started=GetTickCount64();appearance();if(moving)SetTimer(h,1,33,NULL);return 0;
    case WM_TIMER:
        if(moving) {frame((GetTickCount64()-started)/1000.0);InvalidateRect(h,NULL,FALSE);}
        return 0;
    case WM_SETTINGCHANGE:appearance();if(moving)SetTimer(h,1,33,NULL);else KillTimer(h,1);frame(0);InvalidateRect(h,NULL,FALSE);return 0;
    case WM_DISPLAYCHANGE:
        SetWindowPos(h,HWND_BOTTOM,GetSystemMetrics(SM_XVIRTUALSCREEN),GetSystemMetrics(SM_YVIRTUALSCREEN),GetSystemMetrics(SM_CXVIRTUALSCREEN),GetSystemMetrics(SM_CYVIRTUALSCREEN),SWP_NOACTIVATE);return 0;
    case WM_ERASEBKGND:return 1;
    case WM_PAINT: {
        PAINTSTRUCT ps;HDC dc=BeginPaint(h,&ps);RECT r;GetClientRect(h,&r);
        SetStretchBltMode(dc,HALFTONE);SetBrushOrgEx(dc,0,0,NULL);
        StretchDIBits(dc,0,0,r.right,r.bottom,0,0,RW,RH,pixels,&bitmap,DIB_RGB_COLORS,SRCCOPY);
        EndPaint(h,&ps);return 0;
    }
    case WM_NCHITTEST:return HTTRANSPARENT;
    case WM_DESTROY:KillTimer(h,1);PostQuitMessage(0);return 0;
    }
    return DefWindowProcW(h,msg,wp,lp);
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE previous,LPWSTR command,int show) {
    (void)previous;(void)show;
    HANDLE unique=CreateMutexW(NULL,FALSE,L"Local\\TechLoungeOS.PE.Background");
    if(!unique||GetLastError()==ERROR_ALREADY_EXISTS){if(unique)CloseHandle(unique);return 0;}
    bitmap.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);bitmap.bmiHeader.biWidth=RW;bitmap.bmiHeader.biHeight=-RH;bitmap.bmiHeader.biPlanes=1;bitmap.bmiHeader.biBitCount=32;bitmap.bmiHeader.biCompression=BI_RGB;
    WNDCLASSW wc={0};wc.lpfnWndProc=proc;wc.hInstance=instance;wc.lpszClassName=L"TechLoungeOS.PeBackground";
    if(!RegisterClassW(&wc)){CloseHandle(unique);return 1;}
    staticMode=wcsstr(command,L"--static")!=NULL;
    window=CreateWindowExW(WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW|WS_EX_LAYERED|WS_EX_TRANSPARENT,wc.lpszClassName,L"TechLoungeOS setup background",WS_POPUP,GetSystemMetrics(SM_XVIRTUALSCREEN),GetSystemMetrics(SM_YVIRTUALSCREEN),GetSystemMetrics(SM_CXVIRTUALSCREEN),GetSystemMetrics(SM_CYVIRTUALSCREEN),NULL,NULL,instance,NULL);
    if(!window){CloseHandle(unique);return 1;}
    if(wcsstr(command,L"--static"))moving=0;
    frame(0);SetLayeredWindowAttributes(window,0,255,LWA_ALPHA);SetWindowPos(window,HWND_BOTTOM,0,0,0,0,SWP_NOMOVE|SWP_NOSIZE|SWP_NOACTIVATE|SWP_SHOWWINDOW);
    MSG msg;while(GetMessageW(&msg,NULL,0,0)>0){TranslateMessage(&msg);DispatchMessageW(&msg);}
    CloseHandle(unique);return 0;
}
