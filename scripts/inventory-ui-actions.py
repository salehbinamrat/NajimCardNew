"""List source event bindings for release review; this is not a click-test result."""
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
DESKTOP = ROOT / 'src/NajmCard.Desktop'
EVENTS = ('Click', 'SelectionChanged', 'Checked', 'Unchecked', 'SelectedDateChanged',
          'ValueChanged', 'TextChanged', 'PasswordChanged', 'MouseDoubleClick',
          'PreviewMouseWheel', 'PreviewKeyDown', 'KeyDown')
pattern = re.compile(r'([\w.\[\]]+)\s*\.\s*(' + '|'.join(EVENTS) + r')\s*\+=')
bindings = []
for source in sorted(DESKTOP.rglob('*')):
    if any(part in ('bin', 'obj') for part in source.parts):
        continue
    relative = source.relative_to(ROOT).as_posix()
    if source.suffix == '.cs':
        text = source.read_text(encoding='utf-8-sig')
        for match in pattern.finditer(text):
            bindings.append(dict(file=relative, line=text.count('\n', 0, match.start())+1,
                                 control=match[1], event=match[2],
                                 binding=text[match.start():].split('\n', 1)[0].strip()))
    elif source.suffix == '.xaml':
        for element in ET.parse(source).iter():
            for event in EVENTS:
                if event in element.attrib:
                    bindings.append(dict(file=relative,
                        control=element.get('{http://schemas.microsoft.com/winfx/2006/xaml}Name',
                                            element.get('Tag', element.get('Content', element.tag.split('}')[-1]))),
                        event=event, binding=element.get(event)))

destination = Path(sys.argv[1] if len(sys.argv) > 1 else ROOT/'out/ui-action-bindings.json')
destination.parent.mkdir(parents=True, exist_ok=True)
destination.write_text(json.dumps(dict(
    scope='Source event bindings, including shared handlers and generated control factories',
    limitation='Not proof of execution. Match runtime release-ui-audit.json and the field action matrix.',
    count=len(bindings), bindings=bindings), ensure_ascii=False, indent=2), encoding='utf-8')
print(f'{len(bindings)} event bindings inventoried: {destination}')
