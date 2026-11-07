#ifndef TLOS_PREVIEW_SURFACE_H
#define TLOS_PREVIEW_SURFACE_H
#include <windows.h>

typedef struct {HDC dc;HBITMAP bitmap;HGDIOBJ previous;int width,height;} Surface;
static void releaseSurface(Surface *surface){
    if(surface->dc){SelectObject(surface->dc,surface->previous);DeleteObject(surface->bitmap);DeleteDC(surface->dc);}
    *surface=(Surface){0};
}
static int ensureSurface(Surface *surface,HDC reference,int width,int height){
    if(width<=0||height<=0)return 0;
    if(surface->dc&&surface->width==width&&surface->height==height)return 1;
    Surface next={0};next.dc=CreateCompatibleDC(reference);if(!next.dc)return 0;
    next.bitmap=CreateCompatibleBitmap(reference,width,height);
    if(!next.bitmap){DeleteDC(next.dc);return 0;}
    next.previous=SelectObject(next.dc,next.bitmap);next.width=width;next.height=height;
    if(!next.previous||next.previous==HGDI_ERROR){DeleteObject(next.bitmap);DeleteDC(next.dc);return 0;}
    releaseSurface(surface);*surface=next;return 1;
}
#endif
