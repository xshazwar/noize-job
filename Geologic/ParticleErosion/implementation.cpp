
// From : https://github.com/weigert/SimpleHydrology
// https://raw.githubusercontent.com/weigert/SimpleHydrology/master/source/water.h


float heightmap[WSIZE*WSIZE] = {0.0};    //Flat Array

float waterpath[WSIZE*WSIZE] = {0.0};    //Water Path Storage (Rivers)
float waterpool[WSIZE*WSIZE] = {0.0};    //Water Pool Storage (Lakes / Ponds)



void World::erode(int cycles){

  //Track the Movement of all Particles
  float track[dim.x*dim.y] = {0.0f};

  //Do a series of iterations!
  for(int i = 0; i < cycles; i++){

    //Spawn New Particle
    glm::vec2 newpos = glm::vec2(rand()%(int)dim.x, rand()%(int)dim.y);
    Drop drop(newpos);

    while(true){
      while(drop.descend(normal((int)drop.pos.x * dim.y + (int)drop.pos.y), heightmap, waterpath, waterpool, track, plantdensity, dim, SCALE));
      if(!drop.flood(heightmap, waterpool, dim))
        break;
    }
  }

  //Update Path
  float lrate = 0.01;
  for(int i = 0; i < dim.x*dim.y; i++)
    waterpath[i] = (1.0-lrate)*waterpath[i] + lrate*50.0f*track[i]/(1.0f + 50.0f*track[i]);
}
/*
-> pseudocode
float[] maps of res*res for
    heightmap,
    flow -> rivers
    pool -> lakes
    track -> particles?

Erosion cycle

    for i in cycles:
        init particle w/ random pos
        do while canFlood:
            do while canDescend
                canDescend = drop.descend(
                    normal(),
                    heighmap,
                    flow,
                    pool,
                    track,
                    **parameters
                )
            canFlood = drop.flood()
    
    // update waterpath
    for i in res * res:
        flow[i] = (0.99 * flow[i]) + (0.5 track[i] * (1 / ( 1 + 50 * track[i])))

*/

  glm::vec3 normal(int index){

    //Two large triangels adjacent to the plane (+Y -> +X) (-Y -> -X)
    glm::vec3 n = glm::cross(glm::vec3(0.0, SCALE*(heightmap[index+1]-heightmap[index] + waterpool[index+1] - waterpool[index]), 1.0), glm::vec3(1.0, SCALE*(heightmap[index+dim.y]+waterpool[index+dim.y]-heightmap[index]-waterpool[index]), 0.0));
    n += glm::cross(glm::vec3(0.0, SCALE*(heightmap[index-1]-heightmap[index] + waterpool[index-1]-waterpool[index]), -1.0), glm::vec3(-1.0, SCALE*(heightmap[index-dim.y]-heightmap[index]+waterpool[index-dim.y]-waterpool[index]), 0.0));

    //Two Alternative Planes (+X -> -Y) (-X -> +Y)
    n += glm::cross(glm::vec3(1.0, SCALE*(heightmap[index+dim.y]-heightmap[index]+waterpool[index+dim.y]-waterpool[index]), 0.0), glm::vec3(0.0, SCALE*(heightmap[index-1]-heightmap[index]+waterpool[index-1]-waterpool[index]), -1.0));
    n += glm::cross(glm::vec3(-1.0, SCALE*(heightmap[index+dim.y]-heightmap[index]+waterpool[index+dim.y]-waterpool[index]), 0.0), glm::vec3(0.0, SCALE*(heightmap[index+1]-heightmap[index]+waterpool[index+1]-waterpool[index]), 1.0));

    return glm::normalize(n);

  }

/*

    struct WorldTile {
    
        float SCALE;
        int2 res;
        
        NativeArray<float> height;
        NativeArray<float> flow;
        NativeArray<float> pool;
        NativeArray<float> track;

        public float3 Normal(int2 pos){
            // returns normal of the (WIH)
            
            int2 left = new int2(-1, 0);
            int2 right = new int2(1, 0);
            int2 up = new int2(0, 1);
            int2 down = new int2(0, -1);

            float3 n = cross(
                // (0, (WIH(pos + right) - WIH[pos])), 1),
                // (1, (WIH(pos + up) - WIH[pos])), 0)
                diff(0, 1, pos, right),
                diff(1, 0, pos, up)
            );
            n += cross(
                // (0, (WIH(pos + left) - WIH[pos])), -1),
                // (-1, (WIH(pos + down) - WIH[pos])), 0)
                diff(0, -1, pos, left),
                diff(-1, 0, pos, down)
            );

            n += cross(
                // (1, (WIH(pos + up) - WIH[pos])), 0),
                // (0, (WIH(pos + left) - WIH[pos])), -1)
                diff(1, 0, pos, up),
                diff(0, -1, pos, left)
            );

            n += cross(
                // (-1, (WIH(pos + up) - WIH[pos])), 0),
                // (0, (WIH(pos + right) - WIH[pos])), 1)
                diff(-1, 0, pos, up),
                diff(0, 1, pos, right)
            );
            return normalize(n);
        }

        public float3 diff(float x, float z, int2 pos, int2 dir){
            return new float3(x, (WIH(pos + dir) - WIH(pos)), z);
        }

        public float WIH(int idx){
            return height[idx] + pool[idx];
        }

        public float WIH(int2 pos){
            return WIH(getIdx(pos));
        }

        public int getIdx(int2 pos){
            return pos.x + (res.x * pos.y);
        }
    }

*/

struct Drop{
  //Construct Particle at Position
  Drop(glm::vec2 _pos){ pos = _pos; }
  Drop(glm::vec2 _p, glm::ivec2 dim, float v){
    pos = _p;
    int index = _p .x*dim.y+_p.y;
    volume = v;
  }

  //Properties
  int age = 0;
  int index;
  glm::vec2 pos;
  glm::vec2 speed = glm::vec2(0.0);
  float volume = 1.0;   //This will vary in time
  float sediment = 0.0; //Sediment concentration

  //Parameters
  const float density = 1.0;  //This gives varying amounts of inertia and stuff...
  const float evapRate = 0.001;
  const float depositionRate = 1.2*0.08;
  const float minVol = 0.01;
  const float friction = 0.25;
  const float volumeFactor = 0.5; //"Water Deposition Rate"

  //Number of Spills Left
  int spill = 0;

  bool descend(glm::vec3 n, float* h, float* path, float* pool, float* track, float* pd, glm::ivec2 dim, float scale);
  bool flood(float* h, float* pool, glm::ivec2 dim);

  static void cascade(vec2 pos, glm::ivec2 dim, float* h, float* p){

    ivec2 ipos = pos;
    int ind = ipos.x * dim.y + ipos.y;

    if(p[ind] > 0) return; //Don't do this with water

    //Neighbor Positions (8-Way)
    const int nx[8] = {-1,-1,-1, 0, 0, 1, 1, 1};
    const int ny[8] = {-1, 0, 1,-1, 1,-1, 0, 1};

    const float maxdiff = 0.01f;
    const float settling = 0.1f;

    //Iterate over all Neighbors
    for(int m = 0; m < 8; m++){

      ivec2 npos = ipos + ivec2(nx[m], ny[m]);
      int nind = npos.x * dim.y + npos.y;

      if(npos.x >= dim.x || npos.y >= dim.y
         || npos.x < 0 || npos.y < 0) continue;

      if(p[nind] > 0) continue; //Don't do this with water

      //Full Height-Different Between Positions!
      float diff = (h[ind] - h[nind]);
      if(diff == 0)   //No Height Difference
        continue;

      //The Amount of Excess Difference!
      float excess = abs(diff) - maxdiff;
      if(excess <= 0)  //No Excess
        continue;

      //Actual Amount Transferred
      float transfer = settling * excess / 2.0f;

      //Cap by Maximum Transferrable Amount
      if(diff > 0){
        h[ind] -= transfer;
        h[nind] += transfer;
      }
      else{
        h[ind] += transfer;
        h[nind] -= transfer;
      }

    }

  }
/*
cascade(pos, dim, height, pool)
    // get the key for the current position
    idx = world.getIdx(particle.pos)
    
    // if this index is in a known pool stop
    if pool[idx] > 0:
        return;
    
    //Neighbor Positions (8-Way)
    const int nx[8] = {-1,-1,-1, 0, 0, 1, 1, 1};
    const int ny[8] = {-1, 0, 1,-1, 1,-1, 0, 1};

    const float maxdiff = 0.01f;  // maximum diff under which no modification will be made in either direction
    const float settling = 0.1f;

    for each neighbor:
        idxN = lookup neighbor index
        if the neighboor is in the pool:
            continue
        diff = height[idx] - height[idxN]
        if diff == 0:
            continue
        excess = abs(diff) - maxdiff
        if excess <= 0: // no excess
            continue
        
        float transfer = settling * excess / 2.0f;
        
        // Transfer a small amount sediment -> area or lower entropy
        
        //Cap by Maximum Transferrable Amount
        if(diff > 0){
            height[idx] -= transfer;
            height[idxN] += transfer;
        }
        else{
            height[idx] += transfer;
            height[idxN] -= transfer;
        }


*/

};

bool Drop::descend(glm::vec3 n, float* h, float* p, float* b, float* track, float* pd, glm::ivec2 dim, float scale){

  if(volume < minVol)
    return false;

  //Initial Position
  glm::ivec2 ipos = pos;
  int ind = ipos.x*dim.y+ipos.y;

  //Add to Path
  track[ind] += volume;

  //Effective Parameter Set
  /* Higher plant density means less erosion */
  float effD = depositionRate*1.0-pd[ind];//max(0.0, );
  if(effD < 0) effD = 0;

  /* Higher Friction, Lower Evaporation in Streams
  makes particles prefer established streams -> "curvy" */

  float effF = friction*(1.0-p[ind]);
  float effR = evapRate*(1.0-0.2*p[ind]);

  //Particle is Not Accelerated
  if(length(vec2(n.x, n.z))*effF < 1E-5)
    return false;

  speed = mix(vec2(n.x, n.z), speed, effF);
  speed = sqrt(2.0f)*normalize(speed);
  pos   += speed;

  //New Position
  int nind = (int)pos.x*dim.y+(int)pos.y;

  //Out-Of-Bounds
  if(!glm::all(glm::greaterThanEqual(pos, glm::vec2(0))) ||
     !glm::all(glm::lessThan((glm::ivec2)pos, dim))){
       volume = 0.0;
       return false;
   }

   //Particle is in Pool
   if(b[nind] > 0.0){
     return false;
   }

  //Mass-Transfer (in MASS)
  float c_eq = h[ind]-h[nind];
  if(c_eq < 0) c_eq = 0;//max(0.0, (h[ind]-h[nind]));
  float cdiff = c_eq - sediment;
  sediment += effD*cdiff;
  h[ind] -= effD*cdiff;

  //Evaporate (Mass Conservative)
  sediment /= (1.0-effR);
  volume *= (1.0-effR);

  cascade(pos, dim, h, b);

  age++;
  return true;

}

/* 

bool descend( 
    normal,
    // all 4 grids
    heightmap,
    flow,
    pool,
    track,
    // a couple constants
    plantdensity, dim, SCALE )

    // writes track @ this pos
    // writes height @ this pos

    if particle does not have minimum volume:
        return false
    
    // get the key for the current position
    idx = world.getIdx(particle.pos)

    // add this volume to the track at this position
    track[idx] += particle.volume


    // calculate position specific parameters
    // effD(plantDensity[pos]) -> local erosion strength (based on plant density) ***Can ignore? / Punt?***
    // effF(flow[pos]) -> local friction strength
    // effR(flow[pos]) -> local evaporative strength

    //Particle is Not Accelerated
    if(length(vec2(n.x, n.z))*effF < 1E-5)
        return false;

    // Calculate position from speed
    // Can travel anywhere in a radius of sqrt(2) from the current position. Then rounded down.
    // So essentially into any neighbor within one grid square?

    speed = mix(vec2(n.x, n.z), speed, effF);
    speed = sqrt(2.0f)*normalize(speed);
    pos   += speed;

    //Next Index for position
    int nind = (int)pos.x*dim.y+(int)pos.y;

    if next index is out of bounds:
        return false
    
    // if we're entering a known pool stop
    if pool[nind] > 0
        return false
    
    //Mass-Transfer (in MASS)
    float c_eq = height[idx] - height[nind];
    if(c_eq < 0) c_eq = 0;//max(0.0, (height[idx]-height[nind]));

    float cdiff = c_eq - sediment;
    sediment += effD * cdiff;
    height[idx] -= effD * cdiff;

    //Evaporate (Mass Conservative)
    sediment /= (1.0-effR);
    volume *= (1.0-effR);

    cascade(pos, dim, height, pool);

    age++;
    return true;



*/

#include <unordered_map>

/*

Flooding Algorithm Overhaul:
  Currently, I can only flood at my position as long as we are rising.
  Then I return and let the particle descend. This should only happen if I can't find a closed set to fill.

  So: Rise and fill, removing the volume as we go along.
  Then: If we find a lower point, try to rise and fill from there.

*/

bool Drop::flood(float* h, float* p, glm::ivec2 dim){
using namespace glm;

  if(volume < minVol || spill-- <= 0)
    return false;

  //Either try to find a closed set under this plane, which has a certain volume,
  //or raise the plane till we find the correct closed set height.
  //And only if it can't be found, re-emit the particle.

  bool tried[dim.x*dim.y] = {false};

  unordered_map<int, float> boundary;
  vector<ivec2> floodset;

  bool drainfound = false;
  ivec2 drain;

  //Returns whether the set is closed at given height

  const function<bool(ivec2, float)> findset = [&](ivec2 i, float plane){

    if(i.x < 0 || i.y < 0 || i.x >= dim.x || i.y >= dim.y)
      return true;

    int ind = i.x*dim.y+i.y;

    if(tried[ind]) return true;
    tried[ind] = true;

    //Wall / Boundary
    if((h[ind] + p[ind]) > plane){
      boundary[ind] = h[ind] + p[ind];
      return true;
    }

    //Drainage Point
    if((h[ind] + p[ind]) < plane){

      //No Drain yet
      if(!drainfound)
        drain = i;

      //Lower Drain
      else if(p[ind] + h[ind] < p[drain.x*dim.y+drain.y] + h[drain.x*dim.y*drain.y])
        drain = i;

      drainfound = true;
      return false;

    }

    floodset.push_back(i);

    if(!findset(i+ivec2( 1, 0), plane)) return false;
    if(!findset(i-ivec2( 1, 0), plane)) return false;
    if(!findset(i+ivec2( 0, 1), plane)) return false;
    if(!findset(i-ivec2( 0, 1), plane)) return false;
    if(!findset(i+ivec2( 1, 1), plane)) return false;
    if(!findset(i-ivec2( 1, 1), plane)) return false;
    if(!findset(i+ivec2(-1, 1), plane)) return false;
    if(!findset(i-ivec2(-1, 1), plane)) return false;

    return true;

  };

  ivec2 ipos = pos;
  int ind = ipos.x*dim.y+ipos.y;
  float plane = h[ind] + p[ind];

  pair<int, float> minbound = pair<int, float>(ind, plane);

  while(volume > minVol && findset(ipos, plane)){

    //Find the Lowest Element on the Boundary
    minbound = (*boundary.begin());
    for(auto& b : boundary)
    if(b.second < minbound.second)
      minbound = b;

    //Compute the Height of our Volume over the Set
    float vheight = volume*volumeFactor/(float)floodset.size();

    //Not High Enough: Fill 'er up
    if(plane + vheight < minbound.second)
      plane += vheight;

    else{
      volume -= (minbound.second - plane)/volumeFactor*(float)floodset.size();
      plane = minbound.second;
    }

    for(auto& s: floodset)
      p[s.x*dim.y+s.y] = plane - h[s.x*dim.y+s.y];

    boundary.erase(minbound.first);
    tried[minbound.first] = false;
    ipos = ivec2(minbound.first/dim.y, minbound.first%dim.y);

  }

  if(drainfound){

    //Search for Exposed Neighbor with Non-Zero Waterlevel
    const std::function<void(glm::ivec2)> lowbound = [&](glm::ivec2 i){

      //Out-Of-Bounds
      if(i.x < 0 || i.y < 0 || i.x >= dim.x || i.y >= dim.y)
        return;

      if(p[i.x*dim.y+i.y] == 0)
        return;

      //Below Drain Height
      if(h[i.x*dim.y+drain.y] + p[i.x*dim.y+drain.y] < h[drain.x*dim.y+drain.y] + p[drain.x*dim.y+drain.y])
        return;

      //Higher than Plane (we want lower)
      if(h[i.x*dim.y+i.y] + p[i.x*dim.y+i.y] >= plane)
        return;

      plane = h[i.x*dim.y+i.y] + p[i.x*dim.y+i.y];

    };

    lowbound(drain+glm::ivec2(1,0));    //Fill Neighbors
    lowbound(drain-glm::ivec2(1,0));    //Fill Neighbors
    lowbound(drain+glm::ivec2(0,1));    //Fill Neighbors
    lowbound(drain-glm::ivec2(0,1));    //Fill Neighbors
    lowbound(drain+glm::ivec2(1,1));    //Fill Neighbors
    lowbound(drain-glm::ivec2(1,1));    //Fill Neighbors
    lowbound(drain+glm::ivec2(-1,1));    //Fill Neighbors
    lowbound(drain-glm::ivec2(-1,1));    //Fill Neighbors

    float oldvolume = volume;

    //Water-Level to Plane-Height
    for(auto& s: floodset){
      int j = s.x*dim.y+s.y;
    //  volume += ((plane > h[ind])?(h[ind] + p[ind] - plane):p[ind])/volumeFactor;
      p[j] = (plane > h[j])?(plane-h[j]):0.0;
    }

    for(auto& b: boundary){
      int j = b.first;
    //  volume += ((plane > h[ind])?(h[ind] + p[ind] - plane):p[ind])/volumeFactor;
      p[j] = (plane > h[j])?(plane-h[j]):0.0;
    }

//    sediment *= oldvolume/volume;
    sediment /= (float)floodset.size(); //Distribute Sediment in Pool
    pos = drain;

    return true;

  }

  return false;
}

/*
void flood(particle p, heightmap, pool){
    if(p.volume < minVol || p.spill-- <= 0)
        return false;
    
}

*/