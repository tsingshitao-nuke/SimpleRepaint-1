import re

test_input = """@PART[*]:HAS[~SR_Ignore[]]:FOR[zzzzzzSimpleRepaint]
{
\t%SR_WhitelistOnly = #$@SIMPLE_REPAINT_SETTINGS/RepaintWhitelistedPartsOnly$
}
@PART[*]:HAS[~SR_Ignore[],#SR_WhitelistOnly[*rue],~SR_Whitelisted[*rue]]:FOR[zzzzzzSimpleRepaint]
{
\t%SR_Ignore = true
\t%SR_UsePartVariant = false
}

@PART[*]:HAS[~SR_Ignore[],~SR_RepaintType[]]:NEEDS[B9PartSwitch]:FOR[zzzzzzSimpleRepaint]
{
\t%SR_RepaintType = B9PS
\t%SR_MaterialMask1 = *
}
@PART[*]:HAS[#SR_UsePartVariant[true]]:FOR[zzzzzzSimpleRepaint]
{
\t%SR_RepaintType = PartVariant
\t!SR_Ignore = DELETE
}"""

# Test 1: current regex without Multiline
pattern1 = r'^(@(?:PART|MODULE)\[[^\]]*\](?::[^{]+)*)(\s*\{)?'
result1 = re.sub(pattern1, lambda m: m.group(1) + ':NEEDS[!SimpleRepaintCache]' + (m.group(2) or ''), test_input)
print('=== Test 1: without Multiline ===')
print(result1)
print()

# Test 2: with Multiline
result2 = re.sub(pattern1, lambda m: m.group(1) + ':NEEDS[!SimpleRepaintCache]' + (m.group(2) or ''), test_input, flags=re.MULTILINE)
print('=== Test 2: with Multiline ===')
print(result2)
print()

# Test 3: simpler approach - line by line
lines = test_input.split('\n')
result3 = []
for line in lines:
    if re.match(r'^@(?:PART|MODULE)\[.*\]', line):
        stripped = line.rstrip()
        if stripped.endswith('{'):
            result3.append(stripped[:-1].rstrip() + ':NEEDS[!SimpleRepaintCache] {')
        else:
            result3.append(stripped + ':NEEDS[!SimpleRepaintCache]')
    else:
        result3.append(line)
print('=== Test 3: line-by-line ===')
print('\n'.join(result3))
