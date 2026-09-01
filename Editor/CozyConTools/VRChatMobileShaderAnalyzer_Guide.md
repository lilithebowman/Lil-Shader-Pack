# VRChat Mobile Shader Analyzer - User Guide

## Overview

The VRChat Mobile Shader Analyzer is a comprehensive tool designed to safely analyze and replace shaders for VRChat Quest compatibility. It addresses the issues with invisible materials that can occur with aggressive shader replacement by providing detailed analysis, safe replacement options, and automatic backup systems.

## ⚠️ CRITICAL TRANSPARENCY WARNING

**VRChat Mobile has VERY LIMITED transparency support!**

- **ONLY** `VRChat/Mobile/Particles/Alpha Blended` supports true transparency
- Most VRChat Mobile shaders are **opaque-only**
- There are **NO** transparent or cutout variants for most shaders
- Transparent materials **MUST** use particle shaders (intended for particles, not surfaces)

## Key Features

### 🔍 **Intelligent Analysis**

- Analyzes all materials in your scene for VRChat Quest compatibility
- Provides detailed feature comparison between current shaders and VRChat Mobile alternatives
- Identifies which features will be preserved vs. lost during shader replacement
- Color-coded compatibility levels for easy understanding
- **VALIDATES against actual VRChat package shaders** (not assumptions)

### 🛡️ **Safety First**

- Automatic backup creation before any changes
- Smart detection of problematic replacements (transparency mismatches, etc.)
- Manual review warnings for materials that need careful consideration
- Restore from backup functionality
- **Critical transparency warnings** prevent material breakage

### 📊 **Comprehensive Reporting**

- Material-by-material analysis with warnings
- Statistics on shader usage and compatibility
- Critical issues highlighted separately
- Object usage tracking (shows which objects use each material)
- **Accurate shader mappings** based on official VRChat whitelist

## How to Use

### Step 1: Access the Tool

- Go to `Tools > CozyCon > VRChat Mobile Shader Analyzer`
- The analyzer window will open

### Step 2: Analyze Your Scene

- Click **"Analyze All Scene Materials"** to scan everything
- Or select specific objects and click **"Analyze Selected Objects Only"**
- Toggle **"Include Inactive Objects"** if you want to analyze disabled objects too

### Step 3: Review Results

The analyzer will show you:

- **✅ Fully Compatible**: Already using VRChat Mobile shaders
- **🟡 Highly Compatible**: Can be replaced with minimal changes
- **🟠 Partially Compatible**: Will lose some features when replaced
- **🔴 Incompatible**: Major issues or won't work properly
- **❓ Unknown**: Needs manual review

### Step 4: Choose Your Replacement Strategy

#### **Safe Option: "Replace Safe Shaders (High Compatibility Only)"**

- Only replaces materials with minimal risk
- Automatic backup creation
- Best for first-time users
- Recommended for production worlds

#### **Advanced Option: "Replace All Compatible Shaders (With Warnings)"**

- Replaces both highly and partially compatible materials
- Shows warnings for each material before replacement
- Use only if you understand the feature losses
- Requires manual review after replacement

#### **Manual Option: Individual Replacements**

- Each material has its own "Replace" button
- Full control over which materials to change
- Best for fine-tuning specific materials

#### **⚠️ Advanced Option: Custom Shader Replacement**

- Replace ALL materials with any shader of your choice
- Found in the "Custom Shader Replacement" section
- **Use with extreme caution** - can break many materials
- Creates automatic backups before replacement
- Useful for:
  - Applying custom VRChat shaders not in the standard list
  - Converting entire scenes to specific shader types
  - Advanced optimization workflows
- **Recommended only for experienced users**

## Understanding Compatibility Levels

### ✅ **Fully Compatible**

- Already using VRChat Mobile shaders
- No action needed
- Will work perfectly on Quest

### 🟡 **Highly Compatible**

- Can be replaced with VRChat Mobile shaders
- Minimal feature loss
- Example: Standard shader → VRChat/Mobile/Standard Lite
- **Safe for automatic replacement**

### 🟠 **Partially Compatible**

- Can be replaced but will lose some features
- Examples:
  - Advanced metallic effects → simplified diffuse
  - Complex transparency → basic transparency
- **Requires manual review**

### 🔴 **Incompatible**

- Major issues or complete incompatibility
- Examples: UI shaders on 3D objects, particle shaders on static meshes
- **Needs manual material recreation**

### ❓ **Unknown**

- Custom or unrecognized shaders
- Could work but needs testing
- **Manual review strongly recommended**

## What the Analyzer Checks

### **Shader Compatibility**

- VRChat Mobile shader support
- Common Unity shader mappings
- Transparency handling
- Property migration capabilities

### **Material Features**

- Main texture support
- Normal map compatibility
- Emission map limitations
- Transparency handling
- Cutoff/alpha testing
- Metallic/specular workflows

### **Texture Optimization**

- Texture size warnings (>1024px)
- Android compression settings
- Mobile-friendly formats

### **Object Usage**

- Which GameObjects use each material
- Multi-object material sharing
- Scene hierarchy impact

## Official VRChat Mobile Shaders

The analyzer now validates against the **actual VRChat shader whitelist**:

### **Standard Surface Shaders (Opaque Only)**

- `VRChat/Mobile/Standard Lite` - **Primary recommendation** for most materials
- `VRChat/Mobile/Diffuse` - Simple diffuse shading
- `VRChat/Mobile/Bumped Diffuse` - Diffuse with normal mapping
- `VRChat/Mobile/Bumped Mapped Specular` - Specular with normal mapping
- `VRChat/Mobile/MatCap Lit` - MatCap-based lighting
- `VRChat/Mobile/Lightmapped` - For lightmapped surfaces

### **Toon Shaders**

- `VRChat/Mobile/Toon Lit` - Basic toon shading
- `VRChat/Mobile/Toon Standard` - Advanced toon features
- `VRChat/Mobile/Toon Standard (Outline)` - Toon with outlines

### **Transparency Shaders (PARTICLES ONLY!)**

- `VRChat/Mobile/Particles/Alpha Blended` - **ONLY transparency option**
- `VRChat/Mobile/Particles/Additive` - Additive blending
- `VRChat/Mobile/Particles/Multiply` - Multiplicative blending

### **Special Purpose**

- `VRChat/Mobile/Skybox` - For skybox materials
- `VRChat/Mobile/World/Supersampled UI` - For UI elements

## Common Issues and Solutions

### **"Materials Turned Invisible"**

This was a problem with the old shader replacement system. The new analyzer prevents this by:

1. **Transparency Detection**: Detects when materials use transparency and ensures the replacement shader supports it
2. **Property Migration**: Safely transfers compatible properties between shaders
3. **Backup System**: Allows easy restoration if something goes wrong
4. **Manual Review**: Flags risky replacements for manual approval

### **"Lost Visual Quality"**

The analyzer helps minimize quality loss by:

1. **Feature Analysis**: Shows exactly what features will be lost before replacement
2. **Smart Mapping**: Uses the best possible VRChat Mobile shader for each material
3. **Warnings**: Alerts you to materials that will lose significant features
4. **Selective Replacement**: Replace only the materials you're comfortable changing

### **"Don't Know Which Shaders to Replace"**

The analyzer provides clear guidance:

1. **Start with "Safe" replacements** - these have minimal risk
2. **Review "Partially Compatible"** materials individually
3. **Avoid "Incompatible"** materials unless you understand the consequences
4. **Test in VRChat** after any changes

### **"Transparent Materials Broke"**

**This is the #1 issue with VRChat Mobile!**

1. **Only particle shaders support transparency** - there are no transparent variants of surface shaders
2. **Cutout materials must become particles** - VRChat Mobile has no cutout support
3. **Manual review required** - transparency conversion is risky and needs testing
4. **Consider design alternatives** - can you avoid transparency entirely?

## Best Practices

### **Before Replacement**

1. **Create a backup** of your entire project
2. **Test in a copy** of your scene first
3. **Read all warnings** carefully
4. **Understand feature losses** before proceeding

### **During Replacement**

1. **Start with safe materials** only
2. **Replace one category at a time**
3. **Test visual changes** in Unity
4. **Check transparency effects** carefully

### **After Replacement**

1. **Test in VRChat** (both PC and Quest)
2. **Check lighting behavior** in different environments
3. **Verify transparency** works correctly
4. **Use backup** if results aren't satisfactory

## Integration with Other CozyCon Tools

The VRChat Mobile Shader Analyzer works seamlessly with other CozyCon tools:

- **Quest 2 Compatibility Analyzer**: Use together for complete Quest optimization
- **Scene Memory Analyzer**: Check memory usage after shader replacement
- **Custom Lighting Enhancer**: Ensures lighting works with new shaders

## Troubleshooting

### **"Analyzer Shows No Materials"**

- Make sure you have materials in your scene
- Check "Include Inactive Objects" if using disabled objects
- Verify materials are actually assigned to renderers

### **"Backup Restore Doesn't Work"**

- Backups are stored in `Assets/Generated/MaterialBackups/`
- Check this folder exists and contains .mat files
- Materials must have the same name as the backup

### **"Replacement Failed"**

- Check Unity Console for specific error messages
- Verify VRChat SDK shaders are available
- Some shaders might not be installed in your project

### **"Visual Changes After Replacement"**

- This is normal when changing shader types
- Review the warnings shown before replacement
- Use "Restore from Backup" if changes are unacceptable
- Consider manual material adjustment for better results

## Advanced Tips

### **Custom Shader Mappings**

You can modify the shader mappings in the code to handle your specific shaders:

```csharp
// In GetVRChatMobileShaderMappings() method
{ new[] { "YourCustomShader" }, new ShaderMapping {
    shader = "VRChat/Mobile/Standard Lite",
    compatibility = CompatibilityLevel.HighlyCompatible
}}
```

### **Batch Processing**

For large projects:

1. Analyze the entire scene first
2. Replace safe shaders automatically
3. Handle problematic materials in smaller batches
4. Test frequently during the process

### **Quest-Specific Testing**

After shader replacement:

1. Use Unity's Mobile/Quest preview
2. Test actual Quest builds when possible
3. Check performance impact
4. Verify visual quality on mobile hardware

### **Custom Shader Replacement**

The custom shader replacement feature allows you to replace ALL materials with any shader:

**When to use:**

- Applying custom VRChat shaders not in the compatibility list
- Converting entire scenes to use a specific shader family
- Bulk optimization workflows
- Fixing materials with known-good shaders

**How to use safely:**

1. **Always create backups first** - the tool does this automatically
2. **Test on a copy** of your scene first
3. **Choose compatible shaders** - VRChat Mobile shaders are safest
4. **Review results carefully** - some materials may need manual adjustment
5. **Be prepared to restore** from backup if needed

**Best practices:**

- Use VRChat Mobile shaders when possible
- Check transparency settings after replacement
- Test lighting behavior in different scenes
- Monitor console for property migration warnings
- Run analysis again after replacement to verify results

**Warning signs to watch for:**

- Materials appearing completely black or white
- Transparency not working correctly
- Textures not displaying properly
- Console errors about missing properties

## Support

If you encounter issues:

1. Check Unity Console for error messages
2. Verify VRChat SDK is properly installed
3. Ensure materials are saved and not corrupted
4. Try restoring from backup and attempting again
5. Report issues with specific error messages and material details

Remember: The goal is VRChat Quest compatibility while maintaining visual quality. Take your time, test thoroughly, and don't hesitate to use the backup system!
