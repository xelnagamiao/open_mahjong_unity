#ifndef MAHJONG_DORA_SHEEN_INCLUDED
#define MAHJONG_DORA_SHEEN_INCLUDED

// Calibrated against two independent screen captures (72 x 104 face pixels).
// Constant-speed slash: 2.06 s repeat, 15.6 px FWHM, 61.9 degree edge.
// The intensity is constant over time; only its position changes.
float DoraStripe(float2 uv, float seconds) {
    float elapsed = frac(seconds / 2.06) * 2.06;
    float center = 1.35 - elapsed * 4.18;
    float distance = abs(uv.y - 1.299 * uv.x - center) / .181;
    float profile = exp2(-1.442695 * pow(min(distance, 10.0), 2.9));
    return profile * step(0, uv.x) * step(uv.x, 1) * step(0, uv.y) * step(uv.y, 1);
}

float3 DoraToDisplay(float3 value) {
    #ifdef UNITY_COLORSPACE_GAMMA
    return value;
    #else
    return lerp(1.055 * pow(max(value, 0), 1.0 / 2.4) - .055,
        value * 12.92, step(value, .0031308));
    #endif
}

float3 DoraFromDisplay(float3 value) {
    #ifdef UNITY_COLORSPACE_GAMMA
    return value;
    #else
    return lerp(pow(max((value + .055) / 1.055, 0), 2.4),
        value / 12.92, step(value, .04045));
    #endif
}

float3 DoraBrighten(float3 color, float amount) {
    // Add in display space. A linear-space additive overlay changes strength
    // with the underlying ink color and cannot reproduce the measured stripe.
    return DoraFromDisplay(saturate(DoraToDisplay(color) + amount));
}
#endif
