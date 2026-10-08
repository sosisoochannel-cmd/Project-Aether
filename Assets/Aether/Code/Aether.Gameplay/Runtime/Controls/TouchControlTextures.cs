using System.Collections.Generic;
using UnityEngine;

namespace Aether.Gameplay.Controls
{
    public enum TouchControlTextureKind { Left, Right, Jump, Attack, Dodge }

    /// <summary>Creates small, device-independent monochrome textures for each touch control.</summary>
    public static class TouchControlTextures
    {
        private static readonly Dictionary<TouchControlTextureKind, Sprite> Cache = new();

        public static Sprite Get(TouchControlTextureKind kind)
        {
            if (Cache.TryGetValue(kind, out Sprite sprite)) return sprite;
            Texture2D texture = Build(kind, 128);
            texture.name = "Aether_Touch_" + kind;
            sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f), 128f, 0, SpriteMeshType.FullRect);
            sprite.name = "Aether_Touch_" + kind;
            Cache.Add(kind, sprite);
            return sprite;
        }

        private static Texture2D Build(TouchControlTextureKind kind, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color32[] pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255,255,255,0);

            float c = (size - 1) * .5f;
            float outer = size * .47f;
            float inner = size * .43f;
            for (int y=0;y<size;y++) for (int x=0;x<size;x++)
            {
                float dx=x-c, dy=y-c, d=Mathf.Sqrt(dx*dx+dy*dy);
                if (d<=outer && d>=inner) pixels[y*size+x]=new Color32(255,255,255,230);
                if (d<inner*.88f) pixels[y*size+x]=new Color32(255,255,255,34);
            }

            switch(kind)
            {
                case TouchControlTextureKind.Left: DrawChevron(pixels,size,c,c,-1f); break;
                case TouchControlTextureKind.Right: DrawChevron(pixels,size,c,c,1f); break;
                case TouchControlTextureKind.Jump: DrawJump(pixels,size); break;
                case TouchControlTextureKind.Attack: DrawSlash(pixels,size); break;
                case TouchControlTextureKind.Dodge: DrawDiamond(pixels,size); break;
            }
            tex.SetPixels32(pixels); tex.Apply(false,false);
            return tex;
        }

        private static void Put(Color32[] p,int s,int x,int y,byte a=255)
        {
            if(x<0||y<0||x>=s||y>=s)return;
            p[y*s+x]=new Color32(255,255,255,a);
        }
        private static void Thick(Color32[] p,int s,float x,float y,float r)
        {
            int min=Mathf.FloorToInt(x-r),max=Mathf.CeilToInt(x+r);
            for(int yy=min;yy<=max;yy++)for(int xx=min;xx<=max;xx++)
                if((xx-x)*(xx-x)+(yy-y)*(yy-y)<=r*r)Put(p,s,xx,yy);
        }
        private static void DrawChevron(Color32[] p,int s,float cx,float cy,float dir)
        {
            float w=s*.18f;
            for(int i=0;i<34;i++){float t=i/33f; Thick(p,s,cx+dir*(t*w),cy+(t*w),s*.035f); Thick(p,s,cx+dir*(t*w),cy-(t*w),s*.035f);}
        }
        private static void DrawJump(Color32[] p,int s)
        {
            float cx=s*.5f, baseY=s*.42f;
            for(int i=0;i<38;i++){float t=i/37f; float x=cx+(t-.5f)*s*.34f; float y=baseY+Mathf.Sin(t*Mathf.PI)*s*.18f; Thick(p,s,x,y,s*.025f);}
            DrawChevron(p,s,cx,baseY+s*.06f,1f);
            DrawChevron(p,s,cx,baseY+s*.06f,-1f);
        }
        private static void DrawSlash(Color32[] p,int s)
        {
            for(int i=0;i<52;i++){float t=i/51f; Thick(p,s,s*.30f+t*s*.40f,s*.28f+t*s*.44f,s*.032f);}
            for(int i=0;i<38;i++){float t=i/37f; Thick(p,s,s*.38f+t*s*.24f,s*.67f-t*s*.24f,s*.018f);}
        }
        private static void DrawDiamond(Color32[] p,int s)
        {
            float cx=s*.5f,cy=s*.5f,w=s*.17f;
            for(int i=0;i<42;i++){float t=i/41f;float q=(t-.5f)*2f;Thick(p,s,cx+q*w,cy+Mathf.Abs(q)*w,s*.026f);Thick(p,s,cx+q*w,cy-Mathf.Abs(q)*w,s*.026f);}
        }
    }
}
