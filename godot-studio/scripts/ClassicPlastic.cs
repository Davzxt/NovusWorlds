using Godot;

public static class ClassicPlastic
{
    private const string ShaderCode = """
shader_type spatial;
render_mode unshaded, cull_back, depth_draw_opaque;

uniform vec4 albedo_color : source_color = vec4(0.95, 0.85, 0.35, 1.0);
uniform sampler2D albedo_tex : source_color;
uniform bool use_albedo_tex = false;

uniform vec4 specular_color : source_color = vec4(1.0, 1.0, 1.0, 1.0);
uniform float spec_intensity : hint_range(0.0, 2.0) = 0.9;
uniform float shininess : hint_range(1.0, 128.0) = 36.0;

uniform float rim_intensity : hint_range(0.0,1.0) = 0.08;
uniform float rim_power : hint_range(0.5,8.0) = 3.0;

varying vec3 world_position;
varying vec3 world_normal;

void vertex() {
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
    ALPHA_HASH_SCALE = 1.0;
    vec4 base = albedo_color;
    if (use_albedo_tex) {
        base *= texture(albedo_tex, UV);
    }

    vec3 N = normalize(world_normal);
    vec3 V = normalize(CAMERA_POSITION_WORLD - world_position);

    vec3 H = normalize(V + vec3(0.0, 0.0, 1.0));
    float spec = pow(max(dot(N, H), 0.0), shininess);
    vec3 spec_contrib = specular_color.rgb * (spec * spec_intensity);

    float rim = pow(1.0 - max(dot(N, V), 0.0), rim_power) * rim_intensity;
    vec3 rim_contrib = specular_color.rgb * rim;

    ALBEDO = base.rgb + spec_contrib + rim_contrib;
    ALPHA = base.a;
}
""";

    private static Shader? shader;

    public static ShaderMaterial Material(Color color, Texture2D? texture = null, float alpha = 1f)
    {
        shader ??= new Shader { Code = ShaderCode };
        var material = new ShaderMaterial { Shader = shader };
        material.SetShaderParameter("albedo_color", new Color(color.R, color.G, color.B, alpha));
        material.SetShaderParameter("use_albedo_tex", texture != null);
        if (texture != null) material.SetShaderParameter("albedo_tex", texture);
        material.SetShaderParameter("spec_intensity", 0.95f);
        material.SetShaderParameter("shininess", 34f);
        material.SetShaderParameter("rim_intensity", 0.08f);
        return material;
    }
}
