#ifndef TLOS_MOTION_H
#define TLOS_MOTION_H
#include <math.h>

// Exact critically damped motion; retargeting retains the current velocity.
static double spring(double value,double *velocity,double goal,double dt,double frequency){
    double offset=value-goal,term=*velocity+frequency*offset,decay=exp(-frequency*dt);
    *velocity=(*velocity-frequency*term*dt)*decay;
    return goal+(offset+term*dt)*decay;
}
static double unit(double value){return value<0?0:value>1?1:value;}
#endif
