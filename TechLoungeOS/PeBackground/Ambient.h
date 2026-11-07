#ifndef TLOS_AMBIENT_H
#define TLOS_AMBIENT_H
#include <stdint.h>
#include <math.h>
#include <string.h>
#define RW 480
#define RH 270
static void RenderFrame(uint32_t *pixels,double t,int highContrast) {
    if(highContrast){memset(pixels,0,RW*RH*sizeof(uint32_t));return;}
    double a=.46+.16*sin(t*.085),b=.58+.13*cos(t*.061);
    double horizontal[RW],ribbon[RW];
    for(int x=0;x<RW;x++){
        double u=(double)x/RW,dx=(u-a)*1.5;
        horizontal[x]=dx*dx;ribbon[x]=.64+.09*sin(u*5+t*.055);
    }
    for(int y=0;y<RH;y++)for(int x=0;x<RW;x++) {
        double v=(double)y/RH,dy=v-b,band=v-ribbon[x];
        double glow=1.0/(1.0+18*(horizontal[x]+dy*dy));
        double edge=1.0/(1.0+1200*band*band);
        unsigned r=(unsigned)(14+9*glow+5*edge);
        unsigned g=(unsigned)(17+16*glow+11*edge);
        unsigned bl=(unsigned)(23+25*glow+19*edge);
        pixels[y*RW+x]=(r<<16)|(g<<8)|bl;
    }
}
#endif
