// Standalone tests of the exact renderer used by the native PE program. No windows are opened.
#include <windows.h>
#include <stdio.h>
#include "Ambient.h"
static uint32_t first[RW*RH],second[RW*RH];
static int save(const wchar_t *folder,const wchar_t *name,uint32_t *pixels) {
    wchar_t path[MAX_PATH];if(swprintf(path,MAX_PATH,L"%ls\\%ls",folder,name)<0)return 0;
    HANDLE f=CreateFileW(path,GENERIC_WRITE,0,NULL,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,NULL);if(f==INVALID_HANDLE_VALUE)return 0;
    BITMAPFILEHEADER file={0};BITMAPINFOHEADER info={0};DWORD written;
    file.bfType=0x4d42;file.bfOffBits=sizeof(file)+sizeof(info);file.bfSize=file.bfOffBits+sizeof(first);
    info.biSize=sizeof(info);info.biWidth=RW;info.biHeight=-RH;info.biPlanes=1;info.biBitCount=32;info.biCompression=BI_RGB;info.biSizeImage=sizeof(first);
    int ok=WriteFile(f,&file,sizeof(file),&written,NULL)&&written==sizeof(file)&&WriteFile(f,&info,sizeof(info),&written,NULL)&&written==sizeof(info)&&WriteFile(f,pixels,sizeof(first),&written,NULL)&&written==sizeof(first);
    CloseHandle(f);return ok;
}
int wmain(int argc,wchar_t **argv) {
    if(argc==3&&wcscmp(argv[2],L"--preview-frames")==0) {
        for(int i=0;i<360;i++) {
            wchar_t name[64];swprintf(name,64,L"frame-%04d.bmp",i);
            RenderFrame(first,i/30.0,0);
            if(!save(argv[1],name,first))return 4;
        }
        puts("Saved 360 frames from the unchanged native renderer: 12 seconds at 30 fps.");return 0;
    }
    if(argc!=2)return 2;
    RenderFrame(first,0,0);RenderFrame(second,0,0);
    if(memcmp(first,second,sizeof(first)))return 3;
    if(!save(argv[1],L"pe-frame-0.bmp",first))return 4;
    RenderFrame(second,10,0);unsigned changed=0;
    for(int i=0;i<RW*RH;i++)if(first[i]!=second[i])changed++;
    if(changed<RW*RH/2||!save(argv[1],L"pe-frame-10.bmp",second))return 5;
    RenderFrame(second,10,1);for(int i=0;i<RW*RH;i++)if(second[i])return 6;
    ULONGLONG start=GetTickCount64();for(int i=0;i<150;i++)RenderFrame(second,i/30.0,0);
    printf("Renderer checks passed: deterministic frames, %u moving pixels, high-contrast static fallback. 150 frames: %llu ms. No UI window created.\n",changed,GetTickCount64()-start);
    return 0;
}
