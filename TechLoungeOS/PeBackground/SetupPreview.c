// Desktop preview by default; explicit --pe enables the guarded installer handoff.
#ifndef UNICODE
#define UNICODE
#endif
#define _UNICODE
#define COBJMACROS
#include <windows.h>
#include <windowsx.h>
#include <shellapi.h>
#include <commctrl.h>
#include <shlobj.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include "Ambient.h"
#include "InputEditing.h"
#include "Motion.h"
#include "PreviewSurface.h"
#include "FrameClock.h"
#include "SetupHandoff.h"
#include "ToolCatalog.h"

static uint32_t background[RW*RH];
static double page=0, target=0, hover[9]={0}, fieldHover[4]={0}, fieldFocus[4]={0}, typingPulse[4]={0}, checkPose[2]={1,1}, lastTime=0;
static double pageVelocity=0,hoverVelocity[9]={0},focusVelocity[4]={0},fieldVelocity[4]={0},checkVelocity[2]={0},pressPose[7]={0},pressVelocity[7]={0};
static Surface paintSurface={0},transitionSurface={0};
static FrameClock frameClock={0};
static double backgroundFrame=-1;
static int backgroundContrast=-1;
static int automaticSignin=1,helloAfterSetup=1;
static int keyboardFocus=0;
static int measureMode=0;
static int forcedLegacyClock=0;
static int hot=-1, pressed=-1, focus=-1, reduced=0, contrast=0, demo=0, forcedReduced=0, fontVerified=1;
static HFONT fonts[2][49];
static wchar_t fontPaths[2][MAX_PATH];
static wchar_t *metricsPath=NULL;
static unsigned paints=0;
static double paintTotal=0,paintMax=0,started=0;
static double paintSamples[8192],intervalSamples[8192],lastPaint=0;
static unsigned sampleCount=0,intervalCount=0;
static HWND inputs[4];
static HBRUSH editBrush=NULL;
static int liveControls=0,showConfirm=0;
static int pendingFocus=-1;
static RECT savedWorkArea;
static int workAreaChanged=0;
static int toolsOpen=0,toolsPage=0;
static double toolsPose=0,toolsVelocity=0,toolHover[15]={0},toolVelocity[15]={0};
static const COLORREF ink=RGB(239,243,249), muted=RGB(158,171,191), accent=RGB(153,203,251);
static double now(void){ LARGE_INTEGER n,f; QueryPerformanceCounter(&n);QueryPerformanceFrequency(&f);return (double)n.QuadPart/f.QuadPart; }
static COLORREF blend(COLORREF a,COLORREF b,double p){p=unit(p);return RGB((int)(GetRValue(a)+(GetRValue(b)-GetRValue(a))*p),(int)(GetGValue(a)+(GetGValue(b)-GetGValue(a))*p),(int)(GetBValue(a)+(GetBValue(b)-GetBValue(a))*p));}
static double follow(double value,double goal,double dt){return goal+(value-goal)*exp(-dt*20);}
static void rect(HDC dc,int x,int y,int w,int h,int radius,COLORREF fill,COLORREF edge){
 HBRUSH brush=CreateSolidBrush(fill);HPEN pen=CreatePen(PS_SOLID,1,edge);HGDIOBJ ob=SelectObject(dc,brush),op=SelectObject(dc,pen);
 RoundRect(dc,x,y,x+w,y+h,radius*2,radius*2);SelectObject(dc,op);SelectObject(dc,ob);DeleteObject(pen);DeleteObject(brush);
}
static void focusRing(HDC dc,RECT r,int radius,COLORREF colour){
 HPEN pen=CreatePen(PS_SOLID,2,colour);HGDIOBJ p=SelectObject(dc,pen),b=SelectObject(dc,GetStockObject(NULL_BRUSH));
 RoundRect(dc,r.left+3,r.top+3,r.right-3,r.bottom-3,radius*2,radius*2);SelectObject(dc,b);SelectObject(dc,p);DeleteObject(pen);
}
static void text(HDC dc,int x,int y,int w,int h,const wchar_t *s,int size,int bold,COLORREF colour){
 if(size>48)size=48;
 int created=!fonts[bold][size];
 if(created)fonts[bold][size]=CreateFontW(-size,0,0,0,bold?600:400,0,0,0,DEFAULT_CHARSET,OUT_TT_ONLY_PRECIS,CLIP_DEFAULT_PRECIS,ANTIALIASED_QUALITY,DEFAULT_PITCH,L"Manrope");
 HGDIOBJ old=SelectObject(dc,fonts[bold][size]);
 if(created){wchar_t face[64];GetTextFaceW(dc,64,face);if(wcscmp(face,L"Manrope"))fontVerified=0;}
 SetBkMode(dc,TRANSPARENT);SetTextColor(dc,colour);RECT r={x,y,x+w,y+h};DrawTextW(dc,s,-1,&r,DT_LEFT|DT_VCENTER|DT_SINGLELINE|DT_END_ELLIPSIS);SelectObject(dc,old);
}
static RECT bounds(int id,int w,int h){
 int bar=w>900?712:w-48,bx=(w-bar)/2;
 if(id<5){int widths[]={196,112,112,112,112};int x=bx+10;for(int i=0;i<id;i++)x+=widths[i]+6;RECT r={x,h-74,x+widths[id],h-30};return r;}
 int pw=w>820?680:w-64,x=(w-pw)/2,y=(h-440)/2-18;
 if(id>=7){RECT row={x+20,y+(id==7?267:304),x+pw-20,y+(id==7?301:338)};return row;}
 RECT r={id==5?x+24:x+pw-170,y+374,id==5?x+122:x+pw-24,y+416};return r;
}
static void icon(HDC dc,int x,int y,int kind,COLORREF colour){
 HPEN p=CreatePen(PS_SOLID,2,colour);HGDIOBJ old=SelectObject(dc,p),b=SelectObject(dc,GetStockObject(NULL_BRUSH));
 if(kind==0){for(int a=0;a<2;a++)for(int c=0;c<2;c++)Rectangle(dc,x+c*10,y+a*10,x+c*10+7,y+a*10+7);}
 else if(kind==1){MoveToEx(dc,x,y+4,NULL);LineTo(dc,x+7,y+4);LineTo(dc,x+10,y+7);LineTo(dc,x+19,y+7);LineTo(dc,x+19,y+18);LineTo(dc,x,y+18);LineTo(dc,x,y+4);}
 else if(kind==2){MoveToEx(dc,x+2,y+17,NULL);LineTo(dc,x+17,y+2);MoveToEx(dc,x+1,y+3,NULL);LineTo(dc,x+15,y+17);Ellipse(dc,x+12,y,x+21,y+9);}
 else if(kind==3){for(int i=0;i<3;i++)Ellipse(dc,x+i*7,y+8,x+i*7+3,y+11);}
 else {Arc(dc,x+1,y+1,x+19,y+19,x+6,y+2,x+14,y+2);MoveToEx(dc,x+10,y-1,NULL);LineTo(dc,x+10,y+9);}
 SelectObject(dc,b);SelectObject(dc,old);DeleteObject(p);
}
static void button(HDC dc,int id,int w,int h,const wchar_t *label,int primary){
 RECT r=bounds(id,w,h);COLORREF bg=primary?RGB(148,197,244):RGB(30,36,45),fg=primary?RGB(12,26,43):ink;
 if(contrast){bg=GetSysColor(COLOR_BTNFACE);fg=GetSysColor(COLOR_BTNTEXT);}
 else bg=blend(bg,primary?RGB(182,218,252):RGB(48,59,74),hover[id]);
 if(!contrast)bg=blend(bg,RGB(10,23,39),.18*pressPose[id]);
 rect(dc,r.left,r.top,r.right-r.left,r.bottom-r.top,8,bg,primary?bg:RGB(59,70,86));
 int tx=r.left+16;if(id<5){icon(dc,tx,r.top+12,id,fg);tx+=31;}
 text(dc,tx,r.top,r.right-tx-10,r.bottom-r.top,label,14,1,fg);
 if(keyboardFocus&&focus==id)focusRing(dc,r,6,primary?RGB(18,50,79):accent);
}
static void field(HDC dc,int id,int x,int y,int w,const wchar_t *label){
 text(dc,x,y,w,24,label,13,1,contrast?GetSysColor(COLOR_WINDOWTEXT):RGB(195,205,219));
 double strength=fieldFocus[id]*.65+fieldHover[id]*.15+typingPulse[id]*.2;
 rect(dc,x,y+29,w,42,7,contrast?GetSysColor(COLOR_WINDOW):RGB(21,26,34),contrast?GetSysColor(COLOR_WINDOWTEXT):blend(RGB(58,69,85),accent,strength));
 if(liveControls&&!IsWindowVisible(inputs[id])){
   wchar_t current[257]={0};
   if(id>=2){int count=GetWindowTextLengthW(inputs[id]);if(count>256)count=256;for(int i=0;i<count;i++)current[i]=0x2022;}
   else GetWindowTextW(inputs[id],current,257);
   text(dc,x+13,y+29,w-26,42,current,15,0,contrast?GetSysColor(COLOR_WINDOWTEXT):ink);
 }
}
static void tick(HDC dc,int id,int x,int y,int width,const wchar_t *s){
 double pose=checkPose[id-7];COLORREF bg=blend(RGB(29,36,47),accent,pose);
 if(hover[id]>.01)rect(dc,x-4,y-3,width+8,34,6,blend(RGB(24,29,38),RGB(39,50,65),hover[id]),blend(RGB(24,29,38),RGB(56,72,92),hover[id]));
 rect(dc,x,y+5,18,18,5,bg,blend(RGB(112,128,150),accent,pose));HPEN p=CreatePen(PS_SOLID,2,blend(bg,RGB(15,31,49),pose));HGDIOBJ old=SelectObject(dc,p);
 MoveToEx(dc,x+4,y+14,NULL);LineTo(dc,x+8,y+18);LineTo(dc,x+14,y+10);SelectObject(dc,old);DeleteObject(p);text(dc,x+30,y,width-30,29,s,14,0,contrast?GetSysColor(COLOR_WINDOWTEXT):ink);
 if(keyboardFocus&&focus==id){RECT row={x-4,y-3,x+width+4,y+31};focusRing(dc,row,5,accent);}
}
static void backdrop(HDC dc,int w,int h,double t){
 BITMAPINFO bmi={0};bmi.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);bmi.bmiHeader.biWidth=RW;bmi.bmiHeader.biHeight=-RH;bmi.bmiHeader.biPlanes=1;bmi.bmiHeader.biBitCount=32;
 double frame=floor(t*30);
 if(frame!=backgroundFrame||contrast!=backgroundContrast){RenderFrame(background,t,contrast);backgroundFrame=frame;backgroundContrast=contrast;}
 SetStretchBltMode(dc,HALFTONE);StretchDIBits(dc,0,0,w,h,0,0,RW,RH,background,&bmi,DIB_RGB_COLORS,SRCCOPY);
 icon(dc,28,28,0,accent);text(dc,62,20,360,36,L"TechLoungeOS",16,1,ink);text(dc,w-264,23,240,30,peMode?L"Tools: Ctrl + Alt + Home to return":L"WinPE design preview",12,0,muted);
}
static void content(HDC dc,int w,int h,int isAccount){
 COLORREF fg=contrast?GetSysColor(COLOR_WINDOWTEXT):ink,sub=contrast?fg:muted;
 if(!isAccount){
   int x=(w-640)/2,y=h/2-132;
   text(dc,x,y,640,30,L"WINDOWS INSTALLATION",12,1,accent);
   text(dc,x,y+44,660,64,L"A fresh start. Your way.",38,1,fg);
   text(dc,x,y+117,640,30,L"Set up your account, then install Windows.",17,0,sub);
   text(dc,x,y+155,640,28,L"Original setup and recovery tools stay within reach.",14,0,sub);
   rect(dc,x,y+213,300,36,6,RGB(24,32,43),RGB(48,63,82));icon(dc,x+12,y+221,0,accent);text(dc,x+43,y+213,245,36,L"Start with Install Windows below",12,0,RGB(187,204,226));
 }else{
   int pw=w>820?680:w-64,x=(w-pw)/2,y=(h-440)/2-18,col=(pw-72)/2;
   rect(dc,x,y,pw,440,14,contrast?GetSysColor(COLOR_WINDOW):RGB(24,29,38),RGB(66,78,96));
   text(dc,x+24,y+17,pw-48,39,L"Make this PC yours",26,1,fg);text(dc,x+24,y+57,pw-48,26,L"Local account  /  Details for this installation",13,0,sub);
   field(dc,0,x+24,y+101,col,L"Username");field(dc,1,x+48+col,y+101,col,L"PC name (optional)");
   field(dc,2,x+24,y+185,col,L"Password (optional)");
   if(showConfirm)field(dc,3,x+48+col,y+185,col,L"Confirm password");
   tick(dc,7,x+24,y+270,pw-48,L"Sign in automatically when this PC starts");tick(dc,8,x+24,y+307,pw-48,L"Set up Windows Hello after installation");
   text(dc,x+24,y+342,pw-48,24,L"Your PIN stays available for passkeys. PC name defaults to username + PC.",13,0,sub);
   button(dc,5,w,h,L"Back",0);button(dc,6,w,h,L"Continue",1);
 }
}
static RECT toolBounds(int id,int w,int h){
 int pw=w>920?840:w-64,ph=h>620?480:h-140,x=(w-pw)/2,y=(h-96-ph)/2;
 if(id==212){RECT r={x+pw-88,y+14,x+pw-20,y+46};return r;}
 if(id==213){RECT r={x+20,y+ph-46,x+112,y+ph-14};return r;}
 if(id==214){RECT r={x+pw-112,y+ph-46,x+pw-20,y+ph-14};return r;}
 int slot=id-200,col=(pw-52)/2,left=x+20+(slot%2)*(col+12),top=y+78+(slot/2)*54;
 RECT r={left,top,left+col,top+48};return r;
}
static int toolEnabled(int id){return id>=200&&id<212?id-200+toolsPage*12<peToolCount:id==212||(id==213&&toolsPage>0)||(id==214&&(toolsPage+1)*12<peToolCount);}
static void toolButton(HDC dc,RECT r,int id,const wchar_t *label){
 int enabled=toolEnabled(id);COLORREF fill=blend(RGB(28,35,46),RGB(47,61,79),toolHover[id-200]);
 rect(dc,r.left,r.top,r.right-r.left,r.bottom-r.top,7,contrast?GetSysColor(COLOR_BTNFACE):fill,contrast?GetSysColor(COLOR_BTNTEXT):RGB(57,70,87));text(dc,r.left+12,r.top,r.right-r.left-24,r.bottom-r.top,label,13,1,contrast?GetSysColor(enabled?COLOR_BTNTEXT:COLOR_GRAYTEXT):enabled?ink:muted);
 if(keyboardFocus&&focus==id)focusRing(dc,r,6,accent);
}
static void toolsContent(HDC dc,int w,int h){
 int pw=w>920?840:w-64,ph=h>620?480:h-140,x=(w-pw)/2,y=(h-96-ph)/2;
 COLORREF fg=contrast?GetSysColor(COLOR_WINDOWTEXT):ink,sub=contrast?fg:muted;
 rect(dc,x,y,pw,ph,14,contrast?GetSysColor(COLOR_WINDOW):RGB(22,28,37),RGB(66,78,96));
 text(dc,x+22,y+12,pw-130,34,L"Tools and recovery",25,1,fg);
 text(dc,x+22,y+47,pw-44,22,peToolTruncated?L"Some image tools exceed the supported menu size or path length.":L"Original image tools, with their existing launch options.",12,0,sub);
 for(int slot=0;slot<12;slot++){
   int index=toolsPage*12+slot;if(index>=peToolCount)break;
   RECT r=toolBounds(200+slot,w,h);COLORREF fill=blend(RGB(28,35,46),RGB(45,58,76),toolHover[slot]);
   rect(dc,r.left,r.top,r.right-r.left,r.bottom-r.top,8,contrast?GetSysColor(COLOR_WINDOW):fill,contrast?fg:RGB(51,64,82));
   if(peTools[index].icon)DrawIconEx(dc,r.left+13,r.top+14,peTools[index].icon,20,20,0,NULL,DI_NORMAL);else icon(dc,r.left+13,r.top+14,2,accent);
   text(dc,r.left+46,r.top+3,r.right-r.left-57,24,peTools[index].name,13,1,fg);
   text(dc,r.left+46,r.top+26,r.right-r.left-57,19,peTools[index].group,11,0,sub);
   if(keyboardFocus&&focus==200+slot)focusRing(dc,r,6,accent);
 }
 if(!peToolCount)text(dc,x+26,y+120,pw-52,60,peMode?L"No existing tool shortcuts were found. Files and Windows Setup remain available.":L"Available tools are detected from your Windows image when WinPE starts.",14,0,sub);
 toolButton(dc,toolBounds(212,w,h),212,L"Close");toolButton(dc,toolBounds(213,w,h),213,L"Previous");toolButton(dc,toolBounds(214,w,h),214,L"Next");
 wchar_t count[80];swprintf(count,80,L"%d tools  /  Page %d of %d",peToolCount,toolsPage+1,peToolCount?(peToolCount+11)/12:1);
 text(dc,x+pw/2-110,y+ph-46,220,32,count,12,0,sub);
}
static void toolbar(HDC dc,int w,int h){
 int bw=w>900?712:w-48,bx=(w-bw)/2;rect(dc,bx,h-84,bw,64,12,RGB(18,23,31),RGB(51,63,80));
 const wchar_t *labels[]={L"Install Windows",L"Files",L"Tools",L"More",L"Power"};for(int i=0;i<5;i++)button(dc,i,w,h,labels[i],i==0);
}
static void scene(HDC dc,int w,int h,double t){
 backdrop(dc,w,h,t);
 // Crossfade complete surfaces; there is one persistent toolbar, never two.
 if(page<.001||page>.999)content(dc,w,h,page>.5);
 else{
   if(ensureSurface(&transitionSurface,dc,w,h)){
     HDC layer=transitionSurface.dc;backdrop(layer,w,h,t);content(layer,w,h,page>.5);
     double opacity=unit(fabs(page-.5)*2);opacity=opacity*opacity*(3-2*opacity);
     BLENDFUNCTION mix={AC_SRC_OVER,0,(BYTE)(opacity*255),0};AlphaBlend(dc,0,0,w,h,layer,0,0,w,h,mix);
   }else content(dc,w,h,target>.5);
 }
 toolbar(dc,w,h);
 if(toolsPose>.001&&ensureSurface(&transitionSurface,dc,w,h)){
   HDC layer=transitionSurface.dc;backdrop(layer,w,h,t);SetViewportOrgEx(layer,0,(int)((1-toolsPose)*10),NULL);toolsContent(layer,w,h);SetViewportOrgEx(layer,0,0,NULL);toolbar(layer,w,h);
   BLENDFUNCTION mix={AC_SRC_OVER,0,(BYTE)(unit(toolsPose)*255),0};AlphaBlend(dc,0,0,w,h,layer,0,0,w,h,mix);
 }
}
static void clearVelocities(void){
 pageVelocity=0;toolsVelocity=0;memset(toolVelocity,0,sizeof(toolVelocity));memset(hoverVelocity,0,sizeof(hoverVelocity));memset(focusVelocity,0,sizeof(focusVelocity));memset(fieldVelocity,0,sizeof(fieldVelocity));memset(checkVelocity,0,sizeof(checkVelocity));memset(pressVelocity,0,sizeof(pressVelocity));
}
static void settings(void){BOOL enabled=TRUE;HIGHCONTRASTW hc={sizeof(hc),0,NULL};SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION,0,&enabled,0);SystemParametersInfoW(SPI_GETHIGHCONTRAST,sizeof(hc),&hc,0);contrast=(hc.dwFlags&HCF_HIGHCONTRASTON)!=0;reduced=forcedReduced||!enabled||contrast;if(reduced)clearVelocities();}
static int loadFonts(void){
 wchar_t root[MAX_PATH];DWORD n=GetModuleFileNameW(NULL,root,MAX_PATH);if(!n||n>=MAX_PATH)return 0;wchar_t *slash=wcsrchr(root,L'\\');if(!slash)return 0;*slash=0;
 for(int i=0;i<2;i++){if(swprintf(fontPaths[i],MAX_PATH,L"%ls\\Fonts\\manrope-%ls.ttf",root,i?L"semibold":L"regular")<0||!AddFontResourceExW(fontPaths[i],FR_PRIVATE,NULL))return 0;}return 1;
}
#include "SessionFonts.h"
static int compareDouble(const void *a,const void *b){double x=*(const double*)a,y=*(const double*)b;return (x>y)-(x<y);}
static double percentile(double *values,unsigned count,double fraction){if(!count)return 0;qsort(values,count,sizeof(double),compareDouble);return values[(unsigned)((count-1)*fraction)];}
static void cleanup(void){
 restoreSessionFonts();
 if(workAreaChanged)SystemParametersInfoW(SPI_SETWORKAREA,0,&savedWorkArea,SPIF_SENDCHANGE);
 clearTools();
 if(toolComInitialised)CoUninitialize();
 stopClock(&frameClock);
 releaseSurface(&paintSurface);releaseSurface(&transitionSurface);
 if(metricsPath){
   HANDLE file=CreateFileW(metricsPath,GENERIC_WRITE,0,NULL,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,NULL);
   if(file!=INVALID_HANDLE_VALUE){char json[768];int length=snprintf(json,sizeof(json),"{\"paintCount\":%u,\"meanPaintMs\":%.3f,\"p95PaintMs\":%.3f,\"maxPaintMs\":%.3f,\"p50IntervalMs\":%.3f,\"p95IntervalMs\":%.3f,\"elapsedSeconds\":%.3f,\"fontVerified\":%s,\"reducedMotion\":%s}\n",paints,paints?paintTotal/paints:0,percentile(paintSamples,sampleCount,.95),paintMax,percentile(intervalSamples,intervalCount,.5),percentile(intervalSamples,intervalCount,.95),now()-started,fontVerified?"true":"false",reduced?"true":"false");DWORD written;if(length>0&&length<(int)sizeof(json))WriteFile(file,json,(DWORD)length,&written,NULL);CloseHandle(file);}
   LocalFree(metricsPath);
 }
 for(int b=0;b<2;b++){
   for(int s=0;s<49;s++)if(fonts[b][s])DeleteObject(fonts[b][s]);
 }
 for(int i=0;i<2;i++)if(*fontPaths[i])RemoveFontResourceExW(fontPaths[i],FR_PRIVATE,NULL);
 if(editBrush)DeleteObject(editBrush);
}
static int save(const wchar_t *path,int w,int h,int account){
 BITMAPINFO bmi={0};bmi.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);bmi.bmiHeader.biWidth=w;bmi.bmiHeader.biHeight=-h;bmi.bmiHeader.biPlanes=1;bmi.bmiHeader.biBitCount=32;
 void *pixels=NULL;HDC dc=CreateCompatibleDC(NULL);HBITMAP bmp=CreateDIBSection(dc,&bmi,DIB_RGB_COLORS,&pixels,NULL,0);if(!bmp){DeleteDC(dc);return 2;}HGDIOBJ old=SelectObject(dc,bmp);page=account;scene(dc,w,h,0);GdiFlush();
 BITMAPFILEHEADER header={0};header.bfType=0x4D42;header.bfOffBits=sizeof(header)+sizeof(BITMAPINFOHEADER);header.bfSize=header.bfOffBits+w*h*4;HANDLE f=CreateFileW(path,GENERIC_WRITE,0,NULL,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,NULL);int result=2;
 if(f!=INVALID_HANDLE_VALUE){DWORD written;result=!(WriteFile(f,&header,sizeof(header),&written,NULL)&&written==sizeof(header)&&WriteFile(f,&bmi.bmiHeader,sizeof(BITMAPINFOHEADER),&written,NULL)&&written==sizeof(BITMAPINFOHEADER)&&WriteFile(f,pixels,w*h*4,&written,NULL)&&written==(DWORD)(w*h*4));CloseHandle(f);}
 SelectObject(dc,old);DeleteObject(bmp);DeleteDC(dc);return fontVerified?result:3;
}
static void activate(int id){
 if(id>=200&&id<=214){
   if(!toolsOpen||!toolEnabled(id))return;
   if(id==212){toolsOpen=0;focus=2;}
   else if(id==213||id==214){toolsPage+=id==213?-1:1;focus=200;}
   else if(runTool(toolsPage*12+id-200)){toolsOpen=0;focus=2;}
   return;
 }
 if(id==0){toolsOpen=0;target=1;pendingFocus=100;}
 else if(id==5){target=0;pendingFocus=-1;focus=0;}
 else if(id==7)automaticSignin=!automaticSignin;
 else if(id==8)helloAfterSetup=!helloAfterSetup;
 else if(peMode&&id==6&&!setupProcess)startWindowsSetup(setupWindow,inputs,automaticSignin,helloAfterSetup);
 else if(peMode&&id==1){toolsOpen=0;openSourceFiles();}
 else if(id==2){toolsOpen=!toolsOpen;if(toolsOpen){loadTools();toolsPage=0;pendingFocus=-1;focus=peToolCount?200:212;}}
 else if(peMode&&(id==3||id==4)){
   HMENU menu=CreatePopupMenu();
   AppendMenuW(menu,MF_STRING,1,id==4?L"Shut down":L"Setup and recovery tools");
   AppendMenuW(menu,MF_STRING,2,id==4?L"Restart":L"About this setup");POINT point;GetCursorPos(&point);
   int selected=TrackPopupMenu(menu,TPM_RETURNCMD|TPM_RIGHTBUTTON,point.x,point.y,0,setupWindow,NULL);DestroyMenu(menu);
   if(id==3&&selected==1){toolsOpen=1;loadTools();toolsPage=0;focus=peToolCount?200:212;}
   if(id==3&&selected==2)MessageBoxW(setupWindow,L"Your original WinPE tools remain available. The account wizard only prepares the account and PC name; Windows Setup still asks where to install. Press Ctrl + Alt + Home to return from the original tools.",L"TechLoungeOS setup",MB_OK|MB_ICONINFORMATION);
   if(id==4&&selected&&MessageBoxW(setupWindow,L"Leave this session? Unsaved setup choices will be lost.",L"Power",MB_YESNO|MB_ICONQUESTION)==IDYES)
     ShellExecuteW(setupWindow,L"open",L"X:\\Windows\\System32\\wpeutil.exe",selected==1?L"shutdown":L"reboot",NULL,SW_HIDE);
 }
 if(reduced){page=target;checkPose[0]=automaticSignin;checkPose[1]=helloAfterSetup;}
}
static void syncControls(HWND hwnd){
 RECT r;GetClientRect(hwnd,&r);int pw=r.right>820?680:r.right-64,x=(r.right-pw)/2,y=(r.bottom-440)/2-18,col=(pw-72)/2;
 for(int i=0;i<4;i++){
   int left=x+24+(i%2)*(col+24)+13,top=y+(i<2?101:185)+38;
   RECT old;GetWindowRect(inputs[i],&old);MapWindowPoints(NULL,hwnd,(POINT*)&old,2);
   if(old.left!=left||old.top!=top||old.right-old.left!=col-26)MoveWindow(inputs[i],left,top,col-26,26,TRUE);
   int visible=target>.5&&page>.995&&!toolsOpen&&toolsPose<.001&&(i!=3||showConfirm);
   if(((GetWindowLongPtrW(inputs[i],GWL_STYLE)&WS_VISIBLE)!=0)!=visible)ShowWindow(inputs[i],visible?SW_SHOWNA:SW_HIDE);
 }
 if(pendingFocus>=100&&target>.5&&page>.995&&!toolsOpen&&toolsPose<.001){int id=pendingFocus;pendingFocus=-1;focus=-1;SetFocus(inputs[id-100]);}
}
static void stepFocus(HWND hwnd,int backwards){
 if(toolsOpen){
   int order[20],count=0;for(int i=200;i<=214;i++)if(toolEnabled(i))order[count++]=i;for(int i=0;i<5;i++)order[count++]=i;
   int index=-1;for(int i=0;i<count;i++)if(order[i]==focus)index=i;focus=order[index<0?(backwards?count-1:0):(index+(backwards?count-1:1))%count];keyboardFocus=1;SetFocus(hwnd);InvalidateRect(hwnd,NULL,FALSE);return;
 }
 const int accountOrder[]={100,101,102,103,7,8,5,6,0,1,2,3,4};
 int order[13],count=0;
 if(target>.5){for(int i=0;i<13;i++)if(accountOrder[i]!=103||(showConfirm&&GetWindowTextLengthW(inputs[2])>0))order[count++]=accountOrder[i];}
 else for(int i=0;i<5;i++)order[count++]=i;
 HWND active=GetFocus();int current=pendingFocus>=100?pendingFocus:focus;
 for(int i=0;i<4;i++)if(active==inputs[i])current=100+i;
 int index=-1;for(int i=0;i<count;i++)if(order[i]==current)index=i;
 int next=order[index<0?(backwards?count-1:0):(index+(backwards?count-1:1))%count];
 keyboardFocus=1;
 if(next>=100){pendingFocus=next;focus=-1;syncControls(hwnd);}
 else{pendingFocus=-1;focus=next;SetFocus(hwnd);}
 InvalidateRect(hwnd,NULL,FALSE);
}
static int hitControl(HWND hwnd,POINT point){
 RECT client;GetClientRect(hwnd,&client);
 for(int i=0;i<5;i++){RECT r=bounds(i,client.right,client.bottom);if(PtInRect(&r,point))return i;}
 if(toolsOpen){for(int i=200;i<=214;i++){RECT r=toolBounds(i,client.right,client.bottom);if(toolEnabled(i)&&PtInRect(&r,point))return i;}return -1;}
 if(toolsPose>.001)return -1;
 if(target>.5&&page>.995)for(int i=5;i<9;i++){RECT r=bounds(i,client.right,client.bottom);if(PtInRect(&r,point))return i;}
 return -1;
}
static void passwordChanged(HWND hwnd,int leaving){
 int hasPassword=GetWindowTextLengthW(inputs[2])>0;
 if(hasPassword)showConfirm=1;
 else if(leaving){showConfirm=0;SetWindowTextW(inputs[3],L"");}
 syncControls(hwnd);InvalidateRect(hwnd,NULL,FALSE);
}
static int createInputs(HWND hwnd){
 HDC dc=GetDC(hwnd);text(dc,0,0,0,0,L"",15,0,ink);ReleaseDC(hwnd,dc);
 const wchar_t *cues[]={L"Username",L"Uses your username + PC",L"Leave blank for no password",L"Repeat your password"};
 for(int i=0;i<4;i++){
   inputs[i]=CreateWindowExW(0,L"EDIT",L"",WS_CHILD|WS_TABSTOP|ES_AUTOHSCROLL|(i>=2?ES_PASSWORD:0),0,0,10,26,hwnd,(HMENU)(INT_PTR)(100+i),GetModuleHandleW(NULL),NULL);
   if(!inputs[i])return 0;
   SendMessageW(inputs[i],WM_SETFONT,(WPARAM)fonts[0][15],TRUE);SendMessageW(inputs[i],EM_SETLIMITTEXT,i>=2?256:64,0);
   SendMessageW(inputs[i],EM_SETCUEBANNER,TRUE,(LPARAM)cues[i]);
 }
 editBrush=CreateSolidBrush(RGB(21,26,34));liveControls=1;return editBrush!=NULL;
}
static LRESULT CALLBACK proc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp){
 switch(msg){
 case WM_CREATE:settings();if(!createInputs(hwnd))return -1;lastTime=now();SetTimer(hwnd,1,15,NULL);return 0;
 case WM_SIZE:if(liveControls)syncControls(hwnd);InvalidateRect(hwnd,NULL,FALSE);return 0;
 case WM_ENTERSIZEMOVE:if(frameClock.timer)SetTimer(hwnd,1,15,NULL);return 0;
 case WM_EXITSIZEMOVE:if(frameClock.timer){KillTimer(hwnd,1);lastTime=now();frameClock.next=lastTime+frameClock.interval;if(!armClock(&frameClock,lastTime))SetTimer(hwnd,1,15,NULL);}return 0;
 case WM_COMMAND:
   if(liveControls&&LOWORD(wp)>=100&&LOWORD(wp)<=103&&HIWORD(wp)==EN_CHANGE)typingPulse[LOWORD(wp)-100]=1;
   if(liveControls&&LOWORD(wp)==102&&(HIWORD(wp)==EN_CHANGE||HIWORD(wp)==EN_KILLFOCUS))passwordChanged(hwnd,HIWORD(wp)==EN_KILLFOCUS);
   if(reduced)InvalidateRect(hwnd,NULL,FALSE);
   return 0;
 case WM_CTLCOLOREDIT:SetTextColor((HDC)wp,contrast?GetSysColor(COLOR_WINDOWTEXT):ink);SetBkColor((HDC)wp,contrast?GetSysColor(COLOR_WINDOW):RGB(21,26,34));return (LRESULT)(contrast?GetSysColorBrush(COLOR_WINDOW):editBrush);
 case WM_GETMINMAXINFO:{RECT needed={0,0,800,600};AdjustWindowRectEx(&needed,WS_OVERLAPPEDWINDOW,FALSE,0);((MINMAXINFO*)lp)->ptMinTrackSize=(POINT){needed.right-needed.left,needed.bottom-needed.top};return 0;}
 case WM_SETTINGCHANGE:settings();if(reduced)page=target;InvalidateRect(hwnd,NULL,FALSE);return 0;
 case WM_MOUSEMOVE:{POINT p={GET_X_LPARAM(lp),GET_Y_LPARAM(lp)};hot=hitControl(hwnd,p);TRACKMOUSEEVENT track={sizeof(track),TME_LEAVE,hwnd,0};TrackMouseEvent(&track);if(reduced){for(int i=0;i<9;i++)hover[i]=hot==i;for(int i=0;i<15;i++)toolHover[i]=hot==200+i;InvalidateRect(hwnd,NULL,FALSE);}return 0;}
 case WM_MOUSELEAVE:hot=-1;if(reduced){for(int i=0;i<9;i++)hover[i]=0;InvalidateRect(hwnd,NULL,FALSE);}return 0;
 case WM_LBUTTONDOWN:{POINT p={GET_X_LPARAM(lp),GET_Y_LPARAM(lp)};hot=hitControl(hwnd,p);keyboardFocus=0;SetFocus(hwnd);pressed=hot;SetCapture(hwnd);InvalidateRect(hwnd,NULL,FALSE);return 0;}
 case WM_LBUTTONUP:{int clicked=pressed;pressed=-1;ReleaseCapture();if(clicked>=0&&clicked==hot){focus=clicked;activate(clicked);}InvalidateRect(hwnd,NULL,FALSE);return 0;}
 case WM_CAPTURECHANGED:pressed=-1;InvalidateRect(hwnd,NULL,FALSE);return 0;
 case WM_KEYDOWN:keyboardFocus=1;if(wp==VK_TAB)stepFocus(hwnd,(GetKeyState(VK_SHIFT)&0x8000)!=0);else if(wp==VK_ESCAPE){if(toolsOpen){toolsOpen=0;focus=2;}else{target=0;pendingFocus=-1;focus=0;if(reduced)page=0;}}else if(wp==VK_RETURN||wp==VK_SPACE)activate(focus);InvalidateRect(hwnd,NULL,FALSE);return 0;
 case WM_TIMER:{
   // Keep the answer in RAM until reboot: a bootstrap process can finish while
   // Windows Setup's child is still consuming it. Never steal focus from Setup.
   if(setupProcess&&WaitForSingleObject(setupProcess,0)==WAIT_OBJECT_0){CloseHandle(setupProcess);setupProcess=NULL;}
   double t=now(),dt=t-lastTime,oldPage=page,oldTools=toolsPose;lastTime=t;
   if(measureMode&&t-started>=16){PostMessageW(hwnd,WM_CLOSE,0,0);return 0;}
   if(IsIconic(hwnd)||!IsWindowVisible(hwnd))return 0;
   if(dt>.1)dt=.1;
   if(demo)target=fmod(t-started,8)>3?1:0;
   toolsPose=reduced?toolsOpen:spring(toolsPose,&toolsVelocity,toolsOpen,dt,38);
   for(int i=0;i<15;i++)toolHover[i]=reduced?(hot==200+i):spring(toolHover[i],&toolVelocity[i],hot==200+i,dt,48);
   page=reduced?target:spring(page,&pageVelocity,target,dt,38);syncControls(hwnd);
   for(int i=0;i<9;i++)hover[i]=reduced?(hot==i):spring(hover[i],&hoverVelocity[i],hot==i,dt,48);
   for(int i=0;i<7;i++)pressPose[i]=reduced?(pressed==i):spring(pressPose[i],&pressVelocity[i],pressed==i,dt,60);
   POINT mouse;GetCursorPos(&mouse);HWND active=GetFocus();int feedbackChanged=0;
   for(int i=0;i<4;i++){RECT r;GetWindowRect(inputs[i],&r);int over=IsWindowVisible(inputs[i])&&PtInRect(&r,mouse);if(reduced&&(fieldHover[i]!=over||fieldFocus[i]!=(active==inputs[i])))feedbackChanged=1;fieldHover[i]=reduced?over:spring(fieldHover[i],&fieldVelocity[i],over,dt,44);fieldFocus[i]=reduced?(active==inputs[i]):spring(fieldFocus[i],&focusVelocity[i],active==inputs[i],dt,44);typingPulse[i]=reduced?0:follow(typingPulse[i],0,dt);}
   checkPose[0]=reduced?automaticSignin:spring(checkPose[0],&checkVelocity[0],automaticSignin,dt,40);checkPose[1]=reduced?helloAfterSetup:spring(checkPose[1],&checkVelocity[1],helloAfterSetup,dt,40);
   if(!reduced||oldPage!=page||oldTools!=toolsPose||feedbackChanged)InvalidateRect(hwnd,NULL,FALSE);
   return 0;
 }
 case WM_ERASEBKGND:return 1;
 case WM_PAINT:{double begin=now();PAINTSTRUCT ps;HDC dc=BeginPaint(hwnd,&ps);RECT r;GetClientRect(hwnd,&r);if(r.right>0&&r.bottom>0){if(ensureSurface(&paintSurface,dc,r.right,r.bottom)){HDC back=paintSurface.dc;scene(back,r.right,r.bottom,reduced?0:now());BitBlt(dc,0,0,r.right,r.bottom,back,0,0,SRCCOPY);}}EndPaint(hwnd,&ps);double elapsed=(now()-begin)*1000;if(sampleCount<8192)paintSamples[sampleCount++]=elapsed;if(lastPaint>0&&intervalCount<8192)intervalSamples[intervalCount++]=(begin-lastPaint)*1000;lastPaint=begin;paints++;paintTotal+=elapsed;if(elapsed>paintMax)paintMax=elapsed;return 0;}
 case WM_HOTKEY:if(peMode&&wp==1){ShowWindow(hwnd,SW_RESTORE);SetForegroundWindow(hwnd);}return 0;
 case WM_CLOSE:if(peMode){target=0;toolsOpen=0;InvalidateRect(hwnd,NULL,FALSE);return 0;}break;
 case WM_DESTROY:KillTimer(hwnd,1);if(peMode)UnregisterHotKey(hwnd,1);PostQuitMessage(0);return 0;
 }return DefWindowProcW(hwnd,msg,wp,lp);
}
static int motionChecks(void){
 double velocity=0,value=spring(0,&velocity,1,.1,38);
 int ok=fabs(value-(1-4.8*exp(-3.8)))<1e-10;
 double split=0,splitVelocity=0;
 for(int i=0;i<10;i++)split=spring(split,&splitVelocity,1,.01,38);
 ok=ok&&fabs(value-split)<1e-10&&fabs(velocity-splitVelocity)<1e-10;
 double previousVelocity=velocity;
 ok=ok&&spring(value,&velocity,0,0,38)==value&&velocity==previousVelocity;
 double reverse=spring(value,&velocity,0,.001,38);
 ok=ok&&reverse>value; // Velocity does not snap backwards on retarget.
 for(int i=0;i<30;i++)reverse=spring(reverse,&velocity,0,.01,38);
 ok=ok&&reverse>=0&&reverse<.001;
 value=0;velocity=0;
 for(int i=0;i<1000;i++){value=spring(value,&velocity,(i/9)%2,.004,48);ok=ok&&isfinite(value)&&isfinite(velocity)&&value>=0&&value<=1;}
 pageVelocity=40;clearVelocities();ok=ok&&spring(1,&pageVelocity,1,.02,38)==1;
 HDC reference=GetDC(NULL);if(!reference)return 0;
 DWORD before=GetGuiResources(GetCurrentProcess(),GR_GDIOBJECTS);Surface surface={0};
 ok=ok&&ensureSurface(&surface,reference,64,64);
 if(surface.dc){
   HDC cached=surface.dc;HBITMAP bitmap=surface.bitmap;SetPixel(surface.dc,0,0,RGB(12,34,56));
   for(int i=0;i<1000;i++)ok=ok&&ensureSurface(&surface,reference,64,64)&&surface.dc==cached&&surface.bitmap==bitmap;
   ok=ok&&!ensureSurface(&surface,reference,0,64)&&surface.dc==cached&&GetPixel(surface.dc,0,0)==RGB(12,34,56);
   for(int i=0;i<100;i++)ok=ok&&ensureSurface(&surface,reference,i%2?96:64,64);
 }
 releaseSurface(&surface);GdiFlush();ok=ok&&GetGuiResources(GetCurrentProcess(),GR_GDIOBJECTS)==before;ReleaseDC(NULL,reference);
 return ok;
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE previous,LPWSTR command,int show){
 (void)previous;(void)command;SetProcessDPIAware();if(!loadFonts()){MessageBoxW(NULL,L"The preview's bundled Manrope fonts are missing.",L"TechLoungeOS preview",MB_OK|MB_ICONERROR);cleanup();return 3;}
 int argc;LPWSTR *argv=CommandLineToArgvW(GetCommandLineW(),&argc);if(!argv){cleanup();return 2;}
 toolComInitialised=SUCCEEDED(CoInitializeEx(NULL,COINIT_APARTMENTTHREADED));
 if(argc==2&&!wcscmp(argv[1],L"--handofftest")){int ok=handoffChecks();LocalFree(argv);cleanup();return ok?0:7;}
 peMode=argc==2&&!wcscmp(argv[1],L"--pe");
 if(peMode&&!genuinePe()){MessageBoxW(NULL,L"The installer interface runs only in booted WinPE. Open without --pe for the harmless desktop preview.",L"TechLoungeOS",MB_OK|MB_ICONINFORMATION);LocalFree(argv);cleanup();return 8;}
 if(peMode){helloAfterSetup=0;checkPose[1]=0;prepareStandardPeSession();}
 if(argc==2&&!wcscmp(argv[1],L"--motiontest")){int ok=motionChecks();LocalFree(argv);cleanup();return ok?0:6;}
 if(argc==4&&!wcscmp(argv[1],L"--render")){int result=save(argv[2],1280,800,0);if(!result)result=save(argv[3],1280,800,1);LocalFree(argv);cleanup();return result;}
 if(argc==3&&!wcscmp(argv[1],L"--compact")){int result=save(argv[2],800,600,1);LocalFree(argv);cleanup();return result;}
 if(argc==2&&!wcscmp(argv[1],L"--selftest")){double a=follow(0,1,.05),b=follow(a,0,.05);int ok=a>.6&&a<.7&&b>0&&b<a&&fabs(follow(.4,1,0)-.4)<1e-9;for(int i=0;i<9;i++){RECT r=bounds(i,800,600);ok=ok&&r.left>=0&&r.right<=800&&r.top>=0&&r.bottom<=600;}
   ok=ok&&previousWord(L"hello world",11)==6&&previousWord(L"hello world   ",14)==6&&previousWord(L"hello/world",11)==6&&previousWord(L"",0)==0&&previousWord(L"one two",4)==0&&previousWord(L"caf\x00e9 monde",10)==5;
   RECT row=bounds(7,800,600);POINT label={row.right-20,(row.top+row.bottom)/2};ok=ok&&PtInRect(&row,label)&&(row.bottom-row.top)>18;
   for(int i=200;i<=214;i++){RECT r=toolBounds(i,800,600);ok=ok&&r.left>=0&&r.right<=800&&r.top>=0&&r.bottom<516;}
   LocalFree(argv);cleanup();return ok?0:4;}
 int formtest=argc==2&&!wcscmp(argv[1],L"--formtest");
 forcedLegacyClock=argc>=2&&!wcscmp(argv[1],L"--measure-fallback");measureMode=forcedLegacyClock||(argc>=2&&(!wcscmp(argv[1],L"--measure")||!wcscmp(argv[1],L"--measure-static")));forcedReduced=argc>=2&&(!wcscmp(argv[1],L"--demo-static")||!wcscmp(argv[1],L"--measure-static"));demo=measureMode||forcedReduced||(argc>=2&&!wcscmp(argv[1],L"--demo"));if(demo&&argc==3){size_t bytes=(wcslen(argv[2])+1)*sizeof(wchar_t);metricsPath=LocalAlloc(LMEM_FIXED,bytes);if(metricsPath)memcpy(metricsPath,argv[2],bytes);}LocalFree(argv);started=now();WNDCLASSW wc={0};wc.lpfnWndProc=proc;wc.hInstance=instance;wc.lpszClassName=L"TlosSetupPreview";wc.hCursor=LoadCursorW(NULL,IDC_ARROW);if(!RegisterClassW(&wc)){cleanup();return 2;}
 HWND hwnd=CreateWindowExW(0,wc.lpszClassName,peMode?L"TechLoungeOS Windows Setup":L"TechLoungeOS WinPE - design preview",(peMode?WS_POPUP:WS_OVERLAPPEDWINDOW)|WS_CLIPCHILDREN,peMode?0:CW_USEDEFAULT,peMode?0:CW_USEDEFAULT,peMode?GetSystemMetrics(SM_CXSCREEN):1280,peMode?GetSystemMetrics(SM_CYSCREEN):840,NULL,NULL,instance,NULL);if(!hwnd){cleanup();return 2;}
 setupWindow=hwnd;
 if(peMode){
   SetWindowTextW(hwnd,L"TechLoungeOS Windows Setup");show=SW_SHOW;RegisterHotKey(hwnd,1,MOD_CONTROL|MOD_ALT,VK_HOME);
   if(SystemParametersInfoW(SPI_GETWORKAREA,0,&savedWorkArea,0)){
     RECT area={0,0,GetSystemMetrics(SM_CXSCREEN),GetSystemMetrics(SM_CYSCREEN)-96};
     workAreaChanged=SystemParametersInfoW(SPI_SETWORKAREA,0,&area,SPIF_SENDCHANGE)!=0;
   }
 }
 if(formtest){
   page=target=1;syncControls(hwnd);int ok=!showConfirm&&!(GetWindowLongPtrW(inputs[3],GWL_STYLE)&WS_VISIBLE);
   SetWindowTextW(inputs[2],L"fixture-only");ok=ok&&showConfirm&&(GetWindowLongPtrW(inputs[3],GWL_STYLE)&WS_VISIBLE);
   SetWindowTextW(inputs[3],L"stale-confirmation");SetWindowTextW(inputs[2],L"");ok=ok&&showConfirm;
   SendMessageW(hwnd,WM_COMMAND,MAKEWPARAM(102,EN_KILLFOCUS),(LPARAM)inputs[2]);ok=ok&&!showConfirm&&GetWindowTextLengthW(inputs[3])==0&&!(GetWindowLongPtrW(inputs[3],GWL_STYLE)&WS_VISIBLE);
   SetWindowTextW(inputs[2],L"new-fixture");ok=ok&&showConfirm&&GetWindowTextLengthW(inputs[3])==0;
   SetWindowTextW(inputs[2],L"");SendMessageW(hwnd,WM_COMMAND,MAKEWPARAM(102,EN_KILLFOCUS),(LPARAM)inputs[2]);ok=ok&&!showConfirm;
   wchar_t observed[64];SetWindowTextW(inputs[0],L"one two three");SendMessageW(inputs[0],EM_SETSEL,13,13);deleteWord(inputs[0]);GetWindowTextW(inputs[0],observed,64);ok=ok&&!wcscmp(observed,L"one two ");
   SendMessageW(inputs[0],EM_SETSEL,0,3);deleteWord(inputs[0]);GetWindowTextW(inputs[0],observed,64);ok=ok&&!wcscmp(observed,L" two ");
   activate(7);ok=ok&&!automaticSignin;activate(7);ok=ok&&automaticSignin;activate(8);ok=ok&&!helloAfterSetup;activate(8);ok=ok&&helloAfterSetup;
   activate(2);toolsPose=1;syncControls(hwnd);ok=ok&&toolsOpen&&focus==212&&!(GetWindowLongPtrW(inputs[0],GWL_STYLE)&WS_VISIBLE);
   stepFocus(hwnd,0);ok=ok&&focus==0;stepFocus(hwnd,1);ok=ok&&focus==212;
   peToolCount=25;toolsPage=0;activate(214);ok=ok&&toolsPage==1;activate(214);ok=ok&&toolsPage==2&&!toolEnabled(201);activate(214);ok=ok&&toolsPage==2;activate(213);ok=ok&&toolsPage==1;
   activate(212);toolsPose=0;syncControls(hwnd);ok=ok&&!toolsOpen&&(GetWindowLongPtrW(inputs[0],GWL_STYLE)&WS_VISIBLE);clearTools();
   DestroyWindow(hwnd);cleanup();return ok?0:5;
 }
 ShowWindow(hwnd,show);
 if(!forcedLegacyClock&&startClock(&frameClock,hwnd,now()))KillTimer(hwnd,1);
 MSG msg;int running=1;
 while(running){
   DWORD ready=MsgWaitForMultipleObjectsEx(frameClock.timer?1:0,frameClock.timer?&frameClock.timer:NULL,INFINITE,QS_ALLINPUT,MWMO_INPUTAVAILABLE);
   if(ready==WAIT_FAILED){stopClock(&frameClock);SetTimer(hwnd,1,15,NULL);}
   if(frameClock.timer&&ready==WAIT_OBJECT_0){
     SendMessageW(hwnd,WM_TIMER,1,0);UpdateWindow(hwnd);
     frameClock.next+=IsIconic(hwnd)||!IsWindowVisible(hwnd)?.1:frameClock.interval;
     if(!armClock(&frameClock,now()))SetTimer(hwnd,1,15,NULL);
   }
   while(PeekMessageW(&msg,NULL,0,0,PM_REMOVE)){
   if(msg.message==WM_QUIT){running=0;break;}
   if(msg.message==WM_KEYDOWN&&msg.hwnd!=hwnd){
     if(msg.wParam==VK_ESCAPE){SendMessageW(hwnd,WM_KEYDOWN,VK_ESCAPE,0);continue;}
     if(msg.wParam==VK_TAB){stepFocus(hwnd,(GetKeyState(VK_SHIFT)&0x8000)!=0);continue;}
   }
   int input=0;for(int i=0;i<4;i++)if(msg.hwnd==inputs[i])input=1;
   if(input&&editShortcut(hwnd,&msg))continue;
   TranslateMessage(&msg);DispatchMessageW(&msg);
   }
 }
 cleanup();return 0;
}
