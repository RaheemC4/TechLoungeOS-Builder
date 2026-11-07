// Launch the image's existing shortcuts so targets, arguments and working folders
// remain owned by that image. Never copy or rewrite third-party executables.
typedef struct {wchar_t path[MAX_PATH],name[MAX_PATH],group[80];HICON icon;int builtin;} PeTool;
static PeTool peTools[96];
static int peToolCount=0,peToolTruncated=0;
static int toolComInitialised=0;
static void clearTools(void){for(int i=0;i<peToolCount;i++)if(peTools[i].icon)DestroyIcon(peTools[i].icon);memset(peTools,0,sizeof(peTools));peToolCount=0;peToolTruncated=0;}
static void discoverTools(const wchar_t *folder,const wchar_t *group,int depth){
 if(depth>6){peToolTruncated=1;return;}
 wchar_t pattern[MAX_PATH];if(swprintf(pattern,MAX_PATH,L"%ls\\*",folder)<0){peToolTruncated=1;return;}
 WIN32_FIND_DATAW item;HANDLE search=FindFirstFileW(pattern,&item);if(search==INVALID_HANDLE_VALUE)return;
 do{
   if(!wcscmp(item.cFileName,L".")||!wcscmp(item.cFileName,L"..")||(item.dwFileAttributes&FILE_ATTRIBUTE_REPARSE_POINT))continue;
   wchar_t path[MAX_PATH];if(swprintf(path,MAX_PATH,L"%ls\\%ls",folder,item.cFileName)<0){peToolTruncated=1;continue;}
   if(item.dwFileAttributes&FILE_ATTRIBUTE_DIRECTORY){discoverTools(path,item.cFileName,depth+1);continue;}
   const wchar_t *extension=wcsrchr(item.cFileName,L'.');if(!extension||_wcsicmp(extension,L".lnk"))continue;
   // These three navigation actions are replaced by the native tools/setup/power
   // controls; retain and launch their actual leaf tools instead of the old menu.
   if(!_wcsicmp(group,L"Quick access")&&(!_wcsicmp(item.cFileName,L"Start Menu.lnk")||!_wcsicmp(item.cFileName,L"Windows Setup.lnk")||!_wcsicmp(item.cFileName,L"Restart Computer.lnk")))continue;
   int duplicate=0;for(int i=0;i<peToolCount;i++)if(!_wcsicmp(peTools[i].path,path))duplicate=1;if(duplicate)continue;
   if(peToolCount==96){peToolTruncated=1;continue;}
   PeTool *tool=&peTools[peToolCount++];wcscpy(tool->path,path);wcsncpy(tool->name,item.cFileName,(size_t)(extension-item.cFileName));wcsncpy(tool->group,group,79);
   SHFILEINFOW info={0};if(SHGetFileInfoW(path,0,&info,sizeof(info),SHGFI_ICON|SHGFI_SMALLICON))tool->icon=info.hIcon;
 }while(FindNextFileW(search,&item));FindClose(search);
}
static int compareTools(const void *a,const void *b){const PeTool *x=a,*y=b;int group=_wcsicmp(x->group,y->group);return group?group:_wcsicmp(x->name,y->name);}
static const struct {const wchar_t *path,*name;} builtinTools[]={
 {L"X:\\Windows\\System32\\cmd.exe",L"Command prompt"},
 {L"X:\\Windows\\regedit.exe",L"Registry editor"},
 {L"X:\\Windows\\System32\\notepad.exe",L"Notepad"},
 {L"X:\\Windows\\System32\\diskpart.exe",L"Disk partition console"}
};
static int builtinToolPath(const wchar_t *path){
 for(unsigned i=0;i<sizeof(builtinTools)/sizeof(*builtinTools);i++)if(!_wcsicmp(path,builtinTools[i].path))return 1;
 return 0;
}
static int shortcutMatchesBuiltin(const wchar_t *path,const wchar_t *expected);
static void loadTools(void){
 clearTools();if(!peMode)return;
 discoverTools(L"X:\\Windows\\System32\\config\\systemprofile\\AppData\\Roaming\\Microsoft\\Windows\\Start Menu",L"Image tools",0);
 discoverTools(L"X:\\Windows\\System32\\config\\systemprofile\\AppData\\Roaming\\Microsoft\\Internet Explorer\\Quick Launch",L"Quick access",0);
 discoverTools(L"X:\\Windows\\Windows Setup",L"Windows Setup",0);
 discoverTools(L"X:\\ProgramData\\Microsoft\\Windows\\Start Menu\\Programs",L"Windows utilities",0);
 if(!peToolCount||GetFileAttributesW(L"X:\\Windows\\System32\\TechLoungeOS\\startup-original.json")!=INVALID_FILE_ATTRIBUTES){
   for(unsigned i=0;i<sizeof(builtinTools)/sizeof(*builtinTools);i++){
     DWORD attributes=GetFileAttributesW(builtinTools[i].path);if(attributes==INVALID_FILE_ATTRIBUTES||(attributes&FILE_ATTRIBUTE_DIRECTORY))continue;
     int duplicate=0;for(int j=0;j<peToolCount;j++)if(!_wcsicmp(peTools[j].path,builtinTools[i].path)||(!peTools[j].builtin&&shortcutMatchesBuiltin(peTools[j].path,builtinTools[i].path)))duplicate=1;
     if(duplicate)continue;
     if(peToolCount==96){peToolTruncated=1;break;}
     PeTool *tool=&peTools[peToolCount++];wcscpy(tool->path,builtinTools[i].path);wcscpy(tool->name,builtinTools[i].name);wcscpy(tool->group,L"Windows utilities");tool->builtin=1;
     SHFILEINFOW info={0};if(SHGetFileInfoW(tool->path,0,&info,sizeof(info),SHGFI_ICON|SHGFI_SMALLICON))tool->icon=info.hIcon;
   }
 }
 qsort(peTools,peToolCount,sizeof(*peTools),compareTools);
}
static void expandToolPath(const wchar_t *raw,wchar_t *path){
 DWORD count=ExpandEnvironmentStringsW(raw,path,MAX_PATH);if(!count||count>MAX_PATH){*path=0;return;}
 // A few supplied shortcuts were authored on C:, although their bundled tools
 // live on the WinPE RAM drive. Rebase only these two known image directories,
 // and only when that exact executable/directory exists inside booted WinPE.
 if(!_wcsnicmp(path,L"C:\\Program Files\\",17)||!_wcsnicmp(path,L"C:\\PROGRA~1\\",12)||!_wcsnicmp(path,L"C:\\Windows\\",11)){
   wchar_t candidate[MAX_PATH];wcscpy(candidate,path);candidate[0]=L'X';
   if(GetFileAttributesW(candidate)!=INVALID_FILE_ATTRIBUTES)wcscpy(path,candidate);
   else *path=0;
 }
}
static int shortcutMatchesBuiltin(const wchar_t *path,const wchar_t *expected){
 IShellLinkW *link=NULL;IPersistFile *file=NULL;int matches=0;
 if(FAILED(CoCreateInstance(&CLSID_ShellLink,NULL,CLSCTX_INPROC_SERVER,&IID_IShellLinkW,(void**)&link)))return 0;
 if(SUCCEEDED(IShellLinkW_QueryInterface(link,&IID_IPersistFile,(void**)&file))&&SUCCEEDED(IPersistFile_Load(file,path,STGM_READ))){
   wchar_t raw[MAX_PATH]={0},target[MAX_PATH]={0},arguments[4096]={0};
   if(SUCCEEDED(IShellLinkW_GetPath(link,raw,MAX_PATH,NULL,SLGP_RAWPATH))&&*raw&&SUCCEEDED(IShellLinkW_GetArguments(link,arguments,4096))&&!*arguments){
     expandToolPath(raw,target);matches=!*target?0:!_wcsicmp(target,expected);
   }
 }
 if(file)IPersistFile_Release(file);
 IShellLinkW_Release(link);return matches;
}
static int launchShortcut(const wchar_t *path){
 IShellLinkW *link=NULL;IPersistFile *file=NULL;int launched=0;
 if(FAILED(CoCreateInstance(&CLSID_ShellLink,NULL,CLSCTX_INPROC_SERVER,&IID_IShellLinkW,(void**)&link)))return 0;
 if(SUCCEEDED(IShellLinkW_QueryInterface(link,&IID_IPersistFile,(void**)&file))&&SUCCEEDED(IPersistFile_Load(file,path,STGM_READ))){
   wchar_t raw[MAX_PATH]={0},target[MAX_PATH]={0},arguments[4096]={0},rawDirectory[MAX_PATH]={0},directory[MAX_PATH]={0};
   if(SUCCEEDED(IShellLinkW_GetPath(link,raw,MAX_PATH,NULL,SLGP_RAWPATH))&&*raw&&SUCCEEDED(IShellLinkW_GetArguments(link,arguments,4096))&&wcslen(arguments)<4095){
     IShellLinkW_GetWorkingDirectory(link,rawDirectory,MAX_PATH);
     expandToolPath(raw,target);expandToolPath(rawDirectory,directory);
     DWORD attributes=GetFileAttributesW(directory);if(attributes==INVALID_FILE_ATTRIBUTES||!(attributes&FILE_ATTRIBUTE_DIRECTORY))*directory=0;
     // Do not accidentally run a same-named application from an existing C:
     // installation. These entries represent utilities bundled in boot.wim.
     if(!_wcsnicmp(target,L"X:\\",3)&&GetFileAttributesW(target)!=INVALID_FILE_ATTRIBUTES)
       launched=(INT_PTR)ShellExecuteW(setupWindow,L"open",target,arguments,*directory?directory:NULL,SW_SHOWNORMAL)>32;
   }
 }
 if(file)IPersistFile_Release(file);
 IShellLinkW_Release(link);return launched;
}
static int runTool(int index){
 if(!peMode||!genuinePe()||index<0||index>=peToolCount)return 0;
 if(peTools[index].builtin){
   if(builtinToolPath(peTools[index].path)&&(INT_PTR)ShellExecuteW(setupWindow,L"open",peTools[index].path,NULL,NULL,SW_SHOWNORMAL)>32)return 1;
 }else if(launchShortcut(peTools[index].path))return 1;
 MessageBoxW(setupWindow,L"This tool could not start. It may still be initialising or need a component missing from this image. Its original shortcut has been kept unchanged.",L"Image tool",MB_OK|MB_ICONINFORMATION);return 0;
}
