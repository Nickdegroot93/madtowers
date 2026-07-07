using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SecretIslandParallax : BackgroundParalax
{
    public float[] offsets;
    
    protected override float getExtraOffset(int id)
    {
        return offsets[id];
    }
}
