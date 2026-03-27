# Fire Effect Iteration

## Goal

Build a sprite fire effect preview workflow that can produce a convincing "flames on top of / around the sprite" look, not a generic full-rectangle heat/noise fill.

Current preview entry point:
- `Tools > Shader Preview > AllIn1 Effect Preview`

Current implementation inputs:
- Source sprite: `Assets/Sprites/Core/Empty.png`
- Fire template material: `Assets/Plugins/AllIn1SpriteShader/Demo/Materials/Fire.mat`

## Visual Target



# New Shader Code working fire
precision mediump float;
uniform float u_time;
uniform vec2 u_mouse;
uniform vec2 u_resolution;
uniform sampler2D u_texture;
varying vec2 v_uv;

// ======================
// Simplex noise (unchanged)
// ======================
vec3 mod289(vec3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec2 mod289(vec2 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
vec3 permute(vec3 x) { return mod289(((x*34.0)+1.0)*x); }

float snoise(vec2 v) {
    const vec4 C = vec4(0.211324865405187, 0.366025403784439, -0.577350269189626, 0.024390243902439);
    vec2 i = floor(v + dot(v, C.yy));
    vec2 x0 = v - i + dot(i, C.xx);
    vec2 i1 = (x0.x > x0.y) ? vec2(1.0, 0.0) : vec2(0.0, 1.0);
    vec4 x12 = x0.xyxy + C.xxzz;
    x12.xy -= i1;
    i = mod289(i);
    vec3 p = permute(permute(i.y + vec3(0.0, i1.y, 1.0)) + i.x + vec3(0.0, i1.x, 1.0));
    vec3 m = max(0.5 - vec3(dot(x0,x0), dot(x12.xy,x12.xy), dot(x12.zw,x12.zw)), 0.0);
    m = m*m; m = m*m;
    vec3 x = 2.0 * fract(p * C.www) - 1.0;
    vec3 h = abs(x) - 0.5;
    vec3 ox = floor(x + 0.5);
    vec3 a0 = x - ox;
    m *= 1.79284291400159 - 0.85373472095314 * (a0*a0 + h*h);
    vec3 g;
    g.x = a0.x * x0.x + h.x * x0.y;
    g.yz = a0.yz * x12.xz + h.yz * x12.yw;
    return 130.0 * dot(m, g);
}

// ======================
// Hash helper (for ember loop)
// ======================
float hash(vec2 p) {
    p = fract(p * vec2(127.1, 311.7));
    p += dot(p, p + 19.19);
    return fract(p.x * p.y);
}

// ======================
// Improved FBM
// ======================
float fbm(vec2 p, int octaves, float timeOffset) {
    float amp = 0.6;
    float freq = 1.0;
    float sum = 0.0;
    for (int i = 0; i < octaves; i++) {
        vec2 curl = vec2(snoise(p * 0.8 + vec2(timeOffset)), snoise(p * 0.8 + vec2(1.3 + timeOffset))) * 0.15;
        sum += amp * snoise(p * freq + curl);
        amp *= 0.48;
        freq *= 2.1;
    }
    return sum;
}

// ======================
// Sparse flame mask
// ======================
float flameMask(vec2 p) {
    float large = fbm(p * 1.8 + vec2(0.0, -u_time * 0.15), 3, 0.0);
    large = smoothstep(-0.1, 0.4, large);
    float flicker = snoise(p * 12.0 + u_time * 8.0) * 0.15;
    return clamp(large + flicker, 0.0, 1.0);
}

void main() {
    vec2 uv = v_uv;
    vec2 aspect = u_resolution / min(u_resolution.x, u_resolution.y);
    vec2 p = (uv - 0.5) * aspect + 0.5;

    vec4 texColor = texture2D(u_texture, uv);

    float mouseDist = distance(uv, u_mouse);
    float mouseInfluence = smoothstep(0.35, 0.0, mouseDist) * 0.6;

    vec2 flameUV = p * vec2(1.0, 3.0);
    flameUV.y += -u_time * 1.8;
    flameUV.x += sin(u_time * 2.5 + p.y * 8.0) * 0.08;

    float noiseBase = fbm(flameUV * 1.8, 5, u_time * 0.3);
    float noiseDetail = fbm(flameUV * 4.5 + vec2(0.0, u_time * 1.2), 4, u_time * 1.1);
    float noiseTip = snoise(flameUV * 9.0 + vec2(0.0, u_time * 3.5)) * 0.3;

    float flameShape = noiseBase * 0.55 + noiseDetail * 0.35 + noiseTip * 0.1;
    flameShape = pow(max(flameShape, 0.0), 1.8);
    flameShape *= (1.0 - p.y * 1.35);
    flameShape = clamp(flameShape, 0.0, 1.0);

    flameShape *= flameMask(p + vec2(0.0, u_time * 0.1));

    // ======================
    // Small rapid flicks (extra tiny tongue tips)
    // ======================
    vec2 flickUV = p * vec2(1.3, 4.5) + vec2(sin(u_time * 4.2 + p.y * 6.0) * 0.06, -u_time * 4.2);
    float smallFlicks = fbm(flickUV * 3.2, 4, u_time * 2.3);
    smallFlicks = pow(max(smallFlicks * 1.3, 0.0), 2.4);
    smallFlicks *= (1.0 - p.y * 2.1);
    smallFlicks *= flameMask(p * 1.8 + vec2(0.0, u_time * 0.8));
    flameShape += smallFlicks * 0.65;

    flameShape = clamp(flameShape, 0.0, 1.0);
    flameShape += mouseInfluence * flameShape * 1.4;

    // ======================
    // Heat distortion + charring
    // ======================
    float distortion = flameShape * 0.025 * (1.0 + sin(u_time * 12.0) * 0.3);
    vec2 distortedUV = uv + vec2(distortion * snoise(vec2(u_time * 4.0, p.y)), 0.0);
    vec4 texDistorted = texture2D(u_texture, distortedUV);

    float texBright = dot(texDistorted.rgb, vec3(0.299, 0.587, 0.114));
    vec3 charred = texDistorted.rgb * (0.4 + texBright * 0.3);
    charred = mix(charred, vec3(0.05, 0.03, 0.01), smoothstep(0.6, 1.0, flameShape));

    // ======================
    // Flame color
    // ======================
    vec3 fireColor = mix(vec3(0.8, 0.15, 0.0), vec3(1.0, 0.45, 0.05), flameShape);
    fireColor = mix(fireColor, vec3(1.0, 0.85, 0.15), pow(flameShape, 1.6));
    fireColor = mix(fireColor, vec3(1.0, 0.95, 0.6), smoothstep(0.65, 1.0, flameShape));
    fireColor = mix(fireColor, vec3(0.6, 0.9, 1.0), pow(flameShape, 3.0) * 0.15);

    // ======================
    // Main blending
    // ======================
    float flameIntensity = flameShape * (0.65 + texBright * 0.35);
    flameIntensity = clamp(flameIntensity, 0.0, 1.0);

    vec3 color = mix(texDistorted.rgb, charred, smoothstep(0.3, 0.9, flameShape));
    color = mix(color, fireColor, flameIntensity * 0.75);

    // ======================
    // Little sparks + extra tiny flicks
    // ======================

    vec2 sparkUV = p * 38.0 - vec2(0.0, u_time * 22.0);
    float sparks = snoise(sparkUV) * snoise(sparkUV * 1.7 + vec2(13.7));
    sparks = pow(max(sparks, 0.0), 7.0);
    sparks *= smoothstep(0.45, 1.0, flameShape);
    sparks *= (0.6 + 0.4 * sin(u_time * 48.0 + p.x * 120.0));

    color += vec3(1.0, 0.92, 0.65) * sparks * 4.2;

    // Extra micro-flicks on top
    float microFlicks = snoise(p * 28.0 + vec2(0.0, -u_time * 9.0));
    microFlicks = pow(max(microFlicks, 0.0), 5.0) * 0.8;
    microFlicks *= smoothstep(0.5, 1.0, flameShape);
    color += fireColor * microFlicks * 1.1;

    // Tiny glowing embers
    float embers = snoise(p * 25.0 + vec2(0.0, u_time * 15.0)) * 0.5 + 0.5;
    color += fireColor * embers * flameShape * 0.12;

    // ======================
    // Floating ember particles (fixed)
    // ======================
    float aspectRatio = u_resolution.x / u_resolution.y;

    for (int i = 0; i < 8; i++) {
        float fi = float(i);

        float sparkX     = hash(vec2(fi, 10.0));
        float sparkSpeed = 0.3 + hash(vec2(fi, 11.0)) * 0.5;
        // fract() makes each ember loop continuously from bottom to top
        float sparkY     = fract(hash(vec2(fi, 12.0)) + u_time * sparkSpeed);

        vec2 sparkPos = vec2(sparkX, sparkY);
        // Gentle horizontal drift as the ember rises
        sparkPos.x += sin(sparkY * 10.0 + u_time + fi) * 0.05;

        float sparkDist = distance(uv * vec2(aspectRatio, 1.0), sparkPos * vec2(aspectRatio, 1.0));

        // Larger size so sparks are reliably visible (was 0.003 — often sub-pixel)
        float sparkSize = 0.008 + 0.004 * sin(u_time * 10.0 + fi * 3.0);
        float spark = smoothstep(sparkSize, 0.0, sparkDist);

        // Gate on horizontal proximity only — not flameShape,
        // which is ~0 above the flame and would kill every rising spark
        float nearSprite = smoothstep(0.45, 0.0, abs(uv.x - sparkPos.x));
        spark *= nearSprite;

        // Fade out gracefully before wrapping back to the bottom
        spark *= smoothstep(1.0, 0.6, sparkY);

        vec3 sparkColor = mix(vec3(1.0, 0.6, 0.1), vec3(1.0, 1.0, 0.5), hash(vec2(fi, 13.0)));
        color += sparkColor * spark * 1.5;
    }

    gl_FragColor = vec4(color, 1.0);
    #include <colorspace_fragment>
}
