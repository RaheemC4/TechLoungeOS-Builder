#ifndef TLOS_INPUT_EDITING_H
#define TLOS_INPUT_EDITING_H
#include <windows.h>
#include <wchar.h>
#include <string.h>

static int spaceAt(wchar_t c){WORD type=0;GetStringTypeW(CT_CTYPE1,&c,1,&type);return (type&C1_SPACE)!=0;}
static int wordAt(wchar_t c){WORD type=0;GetStringTypeW(CT_CTYPE3,&c,1,&type);return IsCharAlphaNumericW(c)||c==L'_'||(type&C3_NONSPACING)||((unsigned)c>=0xD800&&(unsigned)c<=0xDFFF);}
static DWORD previousWord(const wchar_t *s,DWORD caret){
 DWORD p=caret;while(p&&spaceAt(s[p-1]))p--;
 if(p){int word=wordAt(s[p-1]);while(p&&!spaceAt(s[p-1])&&wordAt(s[p-1])==word)p--;}
 // Never leave a UTF-16 surrogate pair split at a selection boundary.
 if(p&&s[p]>=0xDC00&&s[p]<=0xDFFF&&s[p-1]>=0xD800&&s[p-1]<=0xDBFF)p--;
 return p;
}
static void deleteWord(HWND edit){
 DWORD start=0,end=0;SendMessageW(edit,EM_GETSEL,(WPARAM)&start,(LPARAM)&end);
 if(start==end&&start){
   int length=GetWindowTextLengthW(edit);wchar_t *value=LocalAlloc(LMEM_FIXED,((SIZE_T)length+1)*sizeof(wchar_t));if(!value)return;
   GetWindowTextW(edit,value,length+1);start=previousWord(value,start);SecureZeroMemory(value,((SIZE_T)length+1)*sizeof(wchar_t));LocalFree(value);
   SendMessageW(edit,EM_SETSEL,start,end);
 }
 SendMessageW(edit,EM_REPLACESEL,TRUE,(LPARAM)L"");
}
static int copySelection(HWND owner,HWND edit){
 DWORD start=0,end=0;SendMessageW(edit,EM_GETSEL,(WPARAM)&start,(LPARAM)&end);if(start==end)return 0;
 int length=GetWindowTextLengthW(edit);if(end>(DWORD)length||start>end)return 0;
 SIZE_T bytes=((SIZE_T)length+1)*sizeof(wchar_t);wchar_t *value=LocalAlloc(LMEM_FIXED,bytes);if(!value)return 0;
 GetWindowTextW(edit,value,length+1);SIZE_T selected=((SIZE_T)end-start+1)*sizeof(wchar_t);HGLOBAL memory=GlobalAlloc(GMEM_MOVEABLE,selected);int copied=0;
 if(memory){wchar_t *text=GlobalLock(memory);if(text){memcpy(text,value+start,selected-sizeof(wchar_t));text[end-start]=0;GlobalUnlock(memory);
   int opened=0;for(int attempt=0;attempt<4&&!opened;attempt++){opened=OpenClipboard(owner);if(!opened&&attempt<3)Sleep(5);}
   if(opened){if(EmptyClipboard()&&SetClipboardData(CF_UNICODETEXT,memory)){copied=1;memory=NULL;}CloseClipboard();}
 }if(memory){void *data=GlobalLock(memory);if(data){SecureZeroMemory(data,selected);GlobalUnlock(memory);}GlobalFree(memory);}}
 SecureZeroMemory(value,bytes);LocalFree(value);return copied;
}
static int editShortcut(HWND owner,MSG *msg){
 if(msg->message==WM_CHAR&&msg->wParam==127)return 1;
 if(msg->message!=WM_KEYDOWN||!(GetKeyState(VK_CONTROL)&0x8000))return 0;
 switch(msg->wParam){
 case 'A':SendMessageW(msg->hwnd,EM_SETSEL,0,-1);return 1;
 case VK_BACK:deleteWord(msg->hwnd);return 1;
 case 'C':copySelection(owner,msg->hwnd);return 1;
 case 'X':if(copySelection(owner,msg->hwnd))SendMessageW(msg->hwnd,EM_REPLACESEL,TRUE,(LPARAM)L"");return 1;
 case 'V':SendMessageW(msg->hwnd,WM_PASTE,0,0);return 1;
 }
 return 0;
}
#endif
