// Standard WinPE can expose an italic-only Segoe face. Keep its UI changes
// inside the booted RAM session; original custom-image preferences stay owned.
static NONCLIENTMETRICSW sourceNonclient;
static int sessionFonts[2]={0},sessionMetrics=0;
static void restoreSessionFonts(void){
 if(sessionMetrics)SystemParametersInfoW(SPI_SETNONCLIENTMETRICS,sizeof(sourceNonclient),&sourceNonclient,SPIF_SENDCHANGE);
 sessionMetrics=0;
 for(int i=0;i<2;i++)if(sessionFonts[i]){RemoveFontResourceExW(fontPaths[i],0,NULL);sessionFonts[i]=0;}
}
static void setSessionFace(LOGFONTW *font){
 wcscpy(font->lfFaceName,L"Manrope");font->lfItalic=FALSE;font->lfWeight=font->lfWeight>=600?600:400;
}
static void prepareStandardPeSession(void){
 if(!peMode||!genuinePe()||GetFileAttributesW(L"X:\\Windows\\System32\\TechLoungeOS\\startup-original.json")==INVALID_FILE_ATTRIBUTES)return;
 // Only supply first-run preferences for our separate fallback file manager.
 HKEY key;DWORD disposition;
 if(RegCreateKeyExW(HKEY_CURRENT_USER,L"Software\\Explorer++\\Settings",0,NULL,REG_OPTION_VOLATILE,KEY_SET_VALUE,NULL,&key,&disposition)==ERROR_SUCCESS){
   if(disposition==REG_CREATED_NEW_KEY){DWORD off=0;RegSetValueExW(key,L"ShowFolders",0,REG_DWORD,(BYTE*)&off,sizeof(off));RegSetValueExW(key,L"ShowDisplayWindow",0,REG_DWORD,(BYTE*)&off,sizeof(off));}
   RegCloseKey(key);
 }
 for(int i=0;i<2;i++){
   sessionFonts[i]=AddFontResourceExW(fontPaths[i],0,NULL)>0;
   if(!sessionFonts[i]){restoreSessionFonts();return;}
 }
 HFONT font=CreateFontW(-14,0,0,0,400,FALSE,FALSE,FALSE,DEFAULT_CHARSET,OUT_TT_ONLY_PRECIS,CLIP_DEFAULT_PRECIS,ANTIALIASED_QUALITY,DEFAULT_PITCH,L"Manrope");
 HDC dc=GetDC(NULL);int verified=0;
 if(font&&dc){HGDIOBJ old=SelectObject(dc,font);wchar_t face[64]={0};TEXTMETRICW tm={0};verified=GetTextFaceW(dc,64,face)&&!_wcsicmp(face,L"Manrope")&&GetTextMetricsW(dc,&tm)&&!tm.tmItalic;SelectObject(dc,old);}
 if(dc)ReleaseDC(NULL,dc);
 if(font)DeleteObject(font);
 sourceNonclient.cbSize=sizeof(sourceNonclient);
 if(!verified||!SystemParametersInfoW(SPI_GETNONCLIENTMETRICS,sizeof(sourceNonclient),&sourceNonclient,0)){restoreSessionFonts();return;}
 NONCLIENTMETRICSW updated=sourceNonclient;
 setSessionFace(&updated.lfCaptionFont);setSessionFace(&updated.lfSmCaptionFont);setSessionFace(&updated.lfMenuFont);setSessionFace(&updated.lfStatusFont);setSessionFace(&updated.lfMessageFont);
 // No SPIF_UPDATEINIFILE: do not persist these choices into a user profile.
 sessionMetrics=SystemParametersInfoW(SPI_SETNONCLIENTMETRICS,sizeof(updated),&updated,SPIF_SENDCHANGE)!=0;
 if(!sessionMetrics)restoreSessionFonts();
}
