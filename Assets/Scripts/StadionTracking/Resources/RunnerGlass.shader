Shader "RunnerStats/LiquidGlass"
{
    Properties
    {
        [PerRendererData] _MainTex("Texture", 2D) = "white" {}
        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _ColorMask("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            struct appdata { float4 vertex:POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 shape:TEXCOORD1; float4 flags:TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; float4 color:COLOR; float2 uv:TEXCOORD0; float4 shape:TEXCOORD1; float flat:TEXCOORD2; UNITY_VERTEX_OUTPUT_STEREO };
            v2f vert(appdata v)
            {
                v2f o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex=UnityObjectToClipPos(v.vertex); o.color=v.color; o.uv=v.uv; o.shape=v.shape; o.flat=v.flags.x; return o;
            }
            float rounded(float2 p, float2 halfSize, float radius)
            {
                radius=min(radius,min(halfSize.x,halfSize.y));
                float2 q=abs(p)-halfSize+radius;
                return length(max(q,0))+min(max(q.x,q.y),0)-radius;
            }
            fixed4 frag(v2f i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 size=max(i.shape.xy,1), p=(i.uv-.5)*size;
                float d=rounded(p,size*.5,i.shape.z);
                float aa=max(fwidth(d),.5), inside=1-smoothstep(-aa,aa,d);
                float border=1-smoothstep(.6,1.6,abs(d));
                if(i.flat>.5) return fixed4(1,1,1,(inside*i.color.a+border*.35)*i.color.rgb.r);
                float w=lerp(.07,.24,saturate((1-i.uv.x+i.uv.y)*.5));
                float tint=i.shape.w, a=w+tint*(1-w);
                float3 rgb=(float3(20,12,45)/255*tint*(1-w)+w)/a;
                float gloss=(1-smoothstep(0,1.5,size.y*.5-p.y))*inside*.55;
                rgb=lerp(rgb,1,saturate(border*.35+gloss));
                float shadowD=rounded(p+float2(0,12),size*.5,i.shape.z);
                float shadow=exp(-max(shadowD,0)*max(shadowD,0)/(2*14*14))*.35*(1-inside);
                return fixed4(lerp(float3(10,5,30)/255,rgb,inside),inside*a+shadow);
            }
            ENDCG
        }
    }
}
