import sys
import re

with open(r'F:\Desktop\kaifa\²£Á§±Ê¼Ç\ClassNote.cs', 'r', encoding='utf-8') as f:
    class_note = f.read()

with open(r'f:\Desktop\kaifa\huggingface-server-skill\example\hf-note-app\quicker\HFNoteSync.cs', 'r', encoding='utf-8') as f:
    hf_note = f.read()

# Extract XAML from ClassNote
xaml_start = class_note.find('string xaml = @\"')
xaml_end = class_note.find('\";', xaml_start) + 2
xaml_content = class_note[xaml_start:xaml_end]

# Extract NoteModel from ClassNote if any, or we will just roll our own.
# Wait, let's roll a clean, modern logic based on HFNoteSync.

