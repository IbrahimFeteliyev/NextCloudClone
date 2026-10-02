"""Generate disposable preview fixtures. Requires Pillow and reportlab; video is optional."""
from pathlib import Path
import sys
import zipfile
from PIL import Image, ImageDraw
from reportlab.pdfgen import canvas

target = Path(sys.argv[1])
target.mkdir(parents=True, exist_ok=True)
image = Image.new('RGB', (1600, 1000), '#e4efe7')
draw = ImageDraw.Draw(image)
draw.rounded_rectangle((120, 100, 1480, 900), radius=50, fill='#28664b')
draw.text((230, 380), 'ATLAS / DOCUMENT PREVIEW', fill='white', font_size=68)
draw.text((230, 500), 'Image zoom and responsive fit', fill='#d6ecdd', font_size=42)
image.save(target / 'photo.jpg', quality=90)
pdf = canvas.Canvas(str(target / 'document.pdf'))
for page in (1, 2):
    pdf.setFillColorRGB(.15, .38, .27)
    pdf.setFont('Helvetica-Bold', 28)
    pdf.drawString(65, 735, 'Atlas document preview')
    pdf.setFont('Helvetica', 15)
    pdf.drawString(65, 685, f'Page {page} - PDF.js rendering, navigation and zoom')
    pdf.drawString(65, 650, 'The original document stays securely in MinIO.')
    pdf.showPage()
pdf.save()
(target / 'Q4 planning notes.md').write_text('''# Q4 planning notes

## Priorities
- Launch the document workspace
- Review team permissions
- Share the quarterly forecast

| Team | Status |
| --- | --- |
| Finance | Ready |
| IT | In progress |

```text
Release: October 2026
Owner: Finance team
```

[Project documentation](https://example.com)

<script>alert("unsafe HTML")</script>
[Unsafe link](javascript:alert(1))
''', encoding='utf-8')
(target / 'Welcome to Atlas.txt').write_text('Welcome to Atlas.\n\nYour files stay in your workspace.\nPreview files here; use Download for a local copy.\n\nLine breaks are preserved.\n<script>This is plain text, never HTML.</script>\n', encoding='utf-8')
(target / 'Expense summary.csv').write_text('Department;Expense;Month;Note\nFinance;12400;Sep;"Travel; hotels"\nIT;8300;Sep;"Line one\nLine two"\nHR;4100;Sep;\n', encoding='utf-8')
(target / 'large.log').write_text(''.join(f'{i:06} INFO Safe large-file preview\n' for i in range(40000)), encoding='utf-8')
(target / 'safe-svg.svg').write_text('<svg xmlns="http://www.w3.org/2000/svg" width="900" height="600"><rect width="900" height="600" fill="#e4efe7"/><text x="100" y="280" font-size="42" fill="#28664b">Atlas SVG preview</text><script>alert(1)</script><foreignObject width="50" height="50"><div xmlns="http://www.w3.org/1999/xhtml">unsafe</div></foreignObject></svg>', encoding='utf-8')
with zipfile.ZipFile(target / 'unsupported.zip', 'w') as archive:
    archive.writestr('readme.txt', 'Unsupported format: explicit Download only.')
print(f'Created preview fixtures in {target}')
