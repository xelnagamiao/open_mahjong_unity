import{$ as b,a0 as x,a1 as M,a2 as T,a3 as F}from"./index-CFtSUjDF.js";const O={normal:0,add:1,multiply:2,screen:3,overlay:4,erase:5,"normal-npm":6,"add-npm":7,"screen-npm":8,min:9,max:10},r=0,d=1,u=2,l=3,c=4,h=5,f=class g{constructor(){this.data=0,this.blendMode="normal",this.polygonOffset=0,this.blend=!0,this.depthMask=!0}get blend(){return!!(this.data&1<<r)}set blend(t){!!(this.data&1<<r)!==t&&(this.data^=1<<r)}get offsets(){return!!(this.data&1<<d)}set offsets(t){!!(this.data&1<<d)!==t&&(this.data^=1<<d)}set cullMode(t){if(t==="none"){this.culling=!1;return}this.culling=!0,this.clockwiseFrontFace=t==="front"}get cullMode(){return this.culling?this.clockwiseFrontFace?"front":"back":"none"}get culling(){return!!(this.data&1<<u)}set culling(t){!!(this.data&1<<u)!==t&&(this.data^=1<<u)}get depthTest(){return!!(this.data&1<<l)}set depthTest(t){!!(this.data&1<<l)!==t&&(this.data^=1<<l)}get depthMask(){return!!(this.data&1<<h)}set depthMask(t){!!(this.data&1<<h)!==t&&(this.data^=1<<h)}get clockwiseFrontFace(){return!!(this.data&1<<c)}set clockwiseFrontFace(t){!!(this.data&1<<c)!==t&&(this.data^=1<<c)}get blendMode(){return this._blendMode}set blendMode(t){this.blend=t!=="none",this._blendMode=t,this._blendModeId=O[t]||0}get polygonOffset(){return this._polygonOffset}set polygonOffset(t){this.offsets=!!t,this._polygonOffset=t}toString(){return`[pixi.js/core:State blendMode=${this.blendMode} clockwiseFrontFace=${this.clockwiseFrontFace} culling=${this.culling} depthMask=${this.depthMask} polygonOffset=${this.polygonOffset}]`}static for2d(){const t=new g;return t.depthTest=!1,t.blend=!0,t}};f.default2d=f.for2d();let w=f;const m=class p extends b{constructor(t){t={...p.defaultOptions,...t},super(t),this.enabled=!0,this._state=w.for2d(),this.blendMode=t.blendMode,this.padding=t.padding,typeof t.antialias=="boolean"?this.antialias=t.antialias?"on":"off":this.antialias=t.antialias,this.resolution=t.resolution,this.blendRequired=t.blendRequired,this.clipToViewport=t.clipToViewport,this.addResource("uTexture",0,1),t.blendRequired&&this.addResource("uBackTexture",0,3)}apply(t,i,s,a){t.applyFilter(this,i,s,a)}get blendMode(){return this._state.blendMode}set blendMode(t){this._state.blendMode=t}static from(t){const{gpu:i,gl:s,...a}=t;let n,e;return i&&(n=x.from(i)),s&&(e=M.from(s)),new p({gpuProgram:n,glProgram:e,...a})}};m.defaultOptions={blendMode:"normal",resolution:1,padding:0,antialias:"off",blendRequired:!1,clipToViewport:!0};let P=m;const y=new T;function k(o,t,i,s,a=!1){const n=y;n.minX=0,n.minY=0,n.maxX=o.width/s|0,n.maxY=o.height/s|0;const e=F.getOptimalTexture(n.width,n.height,s,!1,a);return e.source.uploadMethodId="image",e.source.resource=o,e.source.alphaMode="premultiply-alpha-on-upload",e.frame.width=t/s,e.frame.height=i/s,e.source.emit("update",e.source),e.updateUvs(),e}var S=`in vec2 aPosition;
out vec2 vTextureCoord;

uniform vec4 uInputSize;
uniform vec4 uOutputFrame;
uniform vec4 uOutputTexture;

vec4 filterVertexPosition( void )
{
    vec2 position = aPosition * uOutputFrame.zw + uOutputFrame.xy;
    
    position.x = position.x * (2.0 / uOutputTexture.x) - 1.0;
    position.y = position.y * (2.0*uOutputTexture.z / uOutputTexture.y) - uOutputTexture.z;

    return vec4(position, 0.0, 1.0);
}

vec2 filterTextureCoord( void )
{
    return aPosition * (uOutputFrame.zw * uInputSize.zw);
}

void main(void)
{
    gl_Position = filterVertexPosition();
    vTextureCoord = filterTextureCoord();
}
`;export{P as F,w as S,k as g,S as v};
