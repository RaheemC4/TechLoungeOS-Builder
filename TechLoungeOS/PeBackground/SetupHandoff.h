// Deployment actions are available only in an explicitly launched WinPE instance.
// Normal desktop previews and check modes never write an answer file or run Setup.
#include <wchar.h>
#include <wctype.h>
#include <objbase.h>

static int peMode=0;
static HWND setupWindow=NULL;
static HANDLE setupProcess=NULL;
static wchar_t answerPath[MAX_PATH]={0};
static int genuinePe(void);
static void openSourceFiles(void){
 if(!peMode||!genuinePe())return;
 const wchar_t *shortcut=L"X:\\Windows\\System32\\config\\systemprofile\\AppData\\Roaming\\Microsoft\\Internet Explorer\\Quick Launch\\Explorer++.lnk";
 if(GetFileAttributesW(shortcut)!=INVALID_FILE_ATTRIBUTES){
   if((INT_PTR)ShellExecuteW(setupWindow,L"open",shortcut,NULL,NULL,SW_SHOWNORMAL)>32)return;}
 const wchar_t *fallback=L"X:\\Windows\\System32\\TechLoungeOS\\Tools\\Explorer++\\Explorer++.exe";
 if(GetFileAttributesW(fallback)!=INVALID_FILE_ATTRIBUTES&&
    (INT_PTR)ShellExecuteW(setupWindow,L"open",fallback,NULL,NULL,SW_SHOWNORMAL)>32)return;
 MessageBoxW(setupWindow,L"This image's file manager is not available yet. Check Tools for the original utilities, or try again after startup has finished.",L"Files",MB_OK|MB_ICONINFORMATION);
}
static int genuinePe(void){
 HKEY key;wchar_t system[MAX_PATH];
 if(!GetWindowsDirectoryW(system,MAX_PATH)||_wcsnicmp(system,L"X:\\Windows",10))return 0;
 if(RegOpenKeyExW(HKEY_LOCAL_MACHINE,L"SYSTEM\\CurrentControlSet\\Control\\MiniNT",0,KEY_READ,&key)!=ERROR_SUCCESS)return 0;
 RegCloseKey(key);return 1;
}
static int asciiLetter(wchar_t c){return (c>=L'A'&&c<=L'Z')||(c>=L'a'&&c<=L'z');}
static int pcChar(wchar_t c){return asciiLetter(c)||(c>=L'0'&&c<=L'9')||c==L'-';}
static const wchar_t *accountError(const wchar_t *user,wchar_t *pc,const wchar_t *password,const wchar_t *confirmation,int hello){
 size_t n=wcslen(user),length=wcslen(password);
 if(!n||n>20||iswspace(user[0])||iswspace(user[n-1])||user[n-1]==L'.')return L"Enter a username of 1–20 characters without leading or trailing spaces or a final full stop.";
 const wchar_t *reserved[]={L"Administrator",L"Guest",L"DefaultAccount",L"WDAGUtilityAccount",L"DefaultUser0",L"SYSTEM",L"LOCAL SERVICE",L"NETWORK SERVICE"};
 for(unsigned i=0;i<sizeof(reserved)/sizeof(*reserved);i++)if(!_wcsicmp(user,reserved[i]))return L"That username is reserved by Windows. Choose a different name.";
 for(size_t i=0;i<n;i++)if(user[i]<32||user[i]==127||wcschr(L"\"/\\[]:;|=,+*?<>",user[i])||(user[i]>=0xd800&&user[i]<=0xdfff))return L"The username contains punctuation or characters Windows cannot use for an account.";
 if(length>127)return L"The password must contain at most 127 characters.";
 for(size_t i=0;i<length;i++)if(password[i]<32||password[i]==127||(password[i]>=0xd800&&password[i]<=0xdfff))return L"The password contains unsupported characters.";
 if(wcscmp(password,confirmation))return L"The two passwords do not match.";
 if(hello&&!length)return L"To enrol a Windows Hello PIN later, enter an account password. Automatic sign-in can still be enabled.";
 if(!*pc){size_t out=0;for(size_t i=0;i<n;i++)if(pcChar(user[i]))pc[out++]=user[i];pc[out]=0;
   while(out&&pc[out-1]==L'-')pc[--out]=0;
   size_t first=0;while(pc[first]==L'-')first++;if(first)memmove(pc,pc+first,(wcslen(pc+first)+1)*sizeof(wchar_t));
   if(!*pc)wcscpy(pc,L"User");
   if(pc[0]>=L'a'&&pc[0]<=L'z')pc[0]-=32;
   if(wcslen(pc)>13){pc[13]=0;out=13;while(out&&pc[out-1]==L'-')pc[--out]=0;}
   wcscat(pc,L"PC");}
 n=wcslen(pc);int letter=0;
 if(!n||n>15||pc[0]==L'-'||pc[n-1]==L'-')return L"PC name must contain 1–15 letters, numbers or hyphens, with no hyphen at either end.";
 for(size_t i=0;i<n;i++){if(!pcChar(pc[i]))return L"PC name must contain only letters, numbers and hyphens.";letter|=asciiLetter(pc[i]);}
 return letter?NULL:L"PC name must include a letter.";
}
static wchar_t *xmlText(const wchar_t *value){
 size_t n=wcslen(value);wchar_t *result=calloc(n*6+1,sizeof(wchar_t));if(!result)return NULL;
 for(size_t i=0;i<n;i++){const wchar_t *s=NULL;switch(value[i]){case L'&':s=L"&amp;";break;case L'<':s=L"&lt;";break;case L'>':s=L"&gt;";break;case L'\"':s=L"&quot;";break;case L'\'':s=L"&apos;";break;}if(s)wcscat(result,s);else{size_t end=wcslen(result);result[end]=value[i];result[end+1]=0;}}
 return result;
}
static wchar_t *encodedPassword(const wchar_t *password,const wchar_t *suffix){
 static const wchar_t alphabet[]=L"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
 wchar_t joined[144]={0};if(wcslen(password)+wcslen(suffix)>=144)return NULL;wcscpy(joined,password);wcscat(joined,suffix);
 size_t bytes=wcslen(joined)*sizeof(wchar_t),out=0;wchar_t *result=calloc((bytes+2)/3*4+1,sizeof(wchar_t));
 if(result){unsigned char *data=(unsigned char*)joined;for(size_t i=0;i<bytes;i+=3){unsigned a=data[i],b=i+1<bytes?data[i+1]:0,c=i+2<bytes?data[i+2]:0;
 result[out++]=alphabet[a>>2];result[out++]=alphabet[((a&3)<<4)|(b>>4)];result[out++]=i+1<bytes?alphabet[((b&15)<<2)|(c>>6)]:L'=';result[out++]=i+2<bytes?alphabet[c&63]:L'=';}}
 SecureZeroMemory(joined,sizeof(joined));return result;
}
static void eraseText(wchar_t *text){if(text){SecureZeroMemory(text,wcslen(text)*sizeof(wchar_t));free(text);}}
static wchar_t *replaceToken(wchar_t *text,const wchar_t *token,const wchar_t *value){
 size_t count=0,a=wcslen(token),b=wcslen(value),n=wcslen(text);for(wchar_t *p=text;(p=wcsstr(p,token));p+=a)count++;
 if(!count||n>1024*1024||count>8||b>2048){eraseText(text);return NULL;}
 size_t total=n-count*a+count*b;wchar_t *result=calloc(total+1,sizeof(wchar_t));
 if(result){wchar_t *out=result,*p=text,*next;while((next=wcsstr(p,token))){size_t part=(size_t)(next-p);memcpy(out,p,part*sizeof(wchar_t));out+=part;memcpy(out,value,b*sizeof(wchar_t));out+=b;p=next+a;}wcscpy(out,p);}
 eraseText(text);return result;
}
static int appPath(wchar_t *path,const wchar_t *name){DWORD n=GetModuleFileNameW(NULL,path,MAX_PATH);if(!n||n>=MAX_PATH)return 0;wchar_t *slash=wcsrchr(path,L'\\');if(!slash)return 0;*slash=0;return swprintf(slash,MAX_PATH-(slash-path),L"\\%ls",name)>0;}
static wchar_t *readTemplate(const wchar_t *name){
 wchar_t path[MAX_PATH];if(!appPath(path,name))return NULL;HANDLE file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,NULL,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,NULL);if(file==INVALID_HANDLE_VALUE)return NULL;
 DWORD bytes=GetFileSize(file,NULL),read=0;char *utf8=bytes>0&&bytes<=2*1024*1024?calloc(bytes+1,1):NULL;int ok=utf8&&ReadFile(file,utf8,bytes,&read,NULL)&&read==bytes;CloseHandle(file);
 wchar_t *text=NULL;if(ok){int count=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,utf8,bytes,NULL,0);if(count>0){text=calloc(count+1,sizeof(wchar_t));if(text&&!MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,utf8,bytes,text,count)){free(text);text=NULL;}}}if(utf8){SecureZeroMemory(utf8,bytes);free(utf8);}return text;
}
static int findMedia(wchar_t *image){
 wchar_t marker[MAX_PATH];if(!appPath(marker,L"media.id"))return 0;
 char expected[64]={0};DWORD n=0;HANDLE file=CreateFileW(marker,GENERIC_READ,FILE_SHARE_READ,NULL,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,NULL);if(file==INVALID_HANDLE_VALUE)return 0;
 int ok=ReadFile(file,expected,sizeof(expected),&n,NULL)&&n==36;CloseHandle(file);if(!ok)return 0;
 DWORD drives=GetLogicalDrives();int matches=0;
 for(int i=2;i<26;i++)if(i!=23&&(drives&(1u<<i))){wchar_t path[MAX_PATH];swprintf(path,MAX_PATH,L"%lc:\\TechLoungeOS.media",L'A'+i);char actual[64]={0};DWORD bytes=0;
 file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,NULL,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,NULL);if(file==INVALID_HANDLE_VALUE)continue;
 ok=ReadFile(file,actual,sizeof(actual),&bytes,NULL)&&bytes==n&&!memcmp(actual,expected,n);CloseHandle(file);if(!ok)continue;
 // The USB writer splits large WIMs for FAT32. Windows Setup accepts the first
 // install.swm when all parts remain together in sources (Microsoft deployment docs).
 const wchar_t *formats[]={L"wim",L"esd",L"swm"};
 for(int format=0;format<3;format++){swprintf(path,MAX_PATH,L"%lc:\\sources\\install.%ls",L'A'+i,formats[format]);DWORD attributes=GetFileAttributesW(path);if(attributes!=INVALID_FILE_ATTRIBUTES&&!(attributes&FILE_ATTRIBUTE_DIRECTORY)){wcscpy(image,path);matches++;}}}
 return matches==1;
}
static int startWindowsSetup(HWND hwnd,HWND *fields,int automatic,int hello){
 wchar_t user[257]={0},pc[257]={0},password[257]={0},confirmation[257]={0};
 GetWindowTextW(fields[0],user,257);GetWindowTextW(fields[1],pc,257);GetWindowTextW(fields[2],password,257);GetWindowTextW(fields[3],confirmation,257);
 const wchar_t *error=accountError(user,pc,password,confirmation,hello);int ok=0;wchar_t *text=NULL,*escaped=NULL,*encoded=NULL,*autoEncoded=NULL;
 if(error)goto done;
 if(!peMode||!genuinePe()){error=L"Installation is available only inside the Builder's booted WinPE environment.";goto done;}
 wchar_t image[MAX_PATH],setup[MAX_PATH];if(!findMedia(image)){error=L"Connect exactly one copy of this Builder's installation media, then try again.";goto done;}
 if(!GetWindowsDirectoryW(setup,MAX_PATH)||swprintf(setup,MAX_PATH,L"X:\\sources\\setup.exe")<0||GetFileAttributesW(setup)==INVALID_FILE_ATTRIBUTES){error=L"This WinPE has no supported Windows Setup executable. Use its original setup tools.";goto done;}
 text=readTemplate(automatic?L"account-auto.xml":L"account-manual.xml");escaped=xmlText(user);encoded=encodedPassword(password,L"Password");autoEncoded=automatic?encodedPassword(password,L"Password"):NULL;
 if(!text||!escaped||!encoded||(automatic&&!autoEncoded)){error=L"The account setup templates are missing or could not be read.";goto done;}
 text=replaceToken(text,L"TLOS_USER_7D91",escaped);if(text)text=replaceToken(text,L"TLOS-PC-7D91",pc);if(text)text=replaceToken(text,L"TLOS_PASSWORD_7D91",encoded);if(text&&automatic)text=replaceToken(text,L"TLOS_AUTO_PASSWORD_7D91",autoEncoded);if(text)text=replaceToken(text,L"TLOS_HELLO_7D91",hello?L"1":L"0");
 if(!text){error=L"The account setup template was invalid. The installer has not started.";goto done;}
 GUID id;wchar_t guid[40];if(FAILED(CoCreateGuid(&id))||!StringFromGUID2(&id,guid,40)){error=L"Could not create a private setup session.";goto done;}
 swprintf(answerPath,MAX_PATH,L"X:\\Windows\\Temp\\TLOS-%ls.xml",guid);int count=WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,text,-1,NULL,0,NULL,NULL);char *utf8=count>0?calloc(count,1):NULL;
 if(!utf8||!WideCharToMultiByte(CP_UTF8,WC_ERR_INVALID_CHARS,text,-1,utf8,count,NULL,NULL)){free(utf8);error=L"Could not encode the account setup file.";goto done;}
 HANDLE file=CreateFileW(answerPath,GENERIC_WRITE,0,NULL,CREATE_NEW,FILE_ATTRIBUTE_TEMPORARY,NULL);DWORD written=0;
 ok=file!=INVALID_HANDLE_VALUE&&WriteFile(file,utf8,count-1,&written,NULL)&&written==(DWORD)(count-1);if(file!=INVALID_HANDLE_VALUE)CloseHandle(file);SecureZeroMemory(utf8,count);free(utf8);
 if(!ok){DeleteFileW(answerPath);error=L"Could not save the account choices to WinPE memory. The installer has not started.";goto done;}
 wchar_t command[1024];swprintf(command,1024,L"\"%ls\" /unattend:\"%ls\" /installfrom:\"%ls\"",setup,answerPath,image);STARTUPINFOW startup={0};startup.cb=sizeof(startup);PROCESS_INFORMATION process={0};
 ok=CreateProcessW(setup,command,NULL,NULL,FALSE,0,NULL,L"X:\\sources",&startup,&process);
 if(!ok){DeleteFileW(answerPath);error=L"Windows Setup could not start. Your choices remain in the wizard so you can try again.";goto done;}
 CloseHandle(process.hThread);setupProcess=process.hProcess;SetWindowTextW(fields[2],L"");SetWindowTextW(fields[3],L"");
 done:SecureZeroMemory(password,sizeof(password));SecureZeroMemory(confirmation,sizeof(confirmation));eraseText(text);eraseText(escaped);eraseText(encoded);eraseText(autoEncoded);
 if(error)MessageBoxW(hwnd,error,L"Windows setup",MB_OK|MB_ICONINFORMATION);
 return ok;
}
static int handoffChecks(void){
 wchar_t pc[257]=L"";int ok=accountError(L"Raheem",pc,L"",L"",0)==NULL&&!wcscmp(pc,L"RaheemPC");wcscpy(pc,L"");ok=ok&&accountError(L"user",pc,L"",L"",0)==NULL&&!wcscmp(pc,L"UserPC");
 ok=ok&&accountError(L"Administrator",pc,L"",L"",0)!=NULL&&accountError(L"Raheem",pc,L"one",L"two",0)!=NULL&&accountError(L"Raheem",pc,L"",L"",1)!=NULL;
 wchar_t *escaped=xmlText(L"A & B<'\"");ok=ok&&escaped&&!wcscmp(escaped,L"A &amp; B&lt;&apos;&quot;");eraseText(escaped);
 wchar_t *password=encodedPassword(L"",L"Password");ok=ok&&password&&!wcscmp(password,L"UABhAHMAcwB3AG8AcgBkAA==");eraseText(password);
 password=encodedPassword(L"complex_password",L"Password");ok=ok&&password&&!wcscmp(password,L"YwBvAG0AcABsAGUAeABfAHAAYQBzAHMAdwBvAHIAZABQAGEAcwBzAHcAbwByAGQA");eraseText(password);
 *pc=0;ok=ok&&accountError(L"--abcdefghijklmnop",pc,L"",L"",0)==NULL&&!wcscmp(pc,L"AbcdefghijklmPC");
 wchar_t *sample=_wcsdup(L"a TLOS_USER_7D91 b TLOS_USER_7D91");sample=replaceToken(sample,L"TLOS_USER_7D91",L"Raheem &amp; Test");ok=ok&&sample&&!wcscmp(sample,L"a Raheem &amp; Test b Raheem &amp; Test");eraseText(sample);return ok;
}
