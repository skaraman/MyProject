_AY='internalID:'
_AX='00000000000000000000000000000000'
_AW='No categories'
_AV='Please select or enter a category name.'
_AU='Missing category'
_AT='Sprite Library asset (.spriteLib):'
_AS='Missing atlas'
_AR='Missing folder'
_AQ='internalIDToNameTable:'
_AP='count'
_AO='^(.*?)(\\d+)$'
_AN='category'
_AM='missing_target_categories'
_AL='missing_atlas_categories'
_AK='categories_total'
_AJ='values'
_AI='Load Categories'
_AH='spriteSheet:'
_AG='nameFileIdTable:'
_AF='transparency'
_AE='base'
_AD='\r\n'
_AC='norm'
_AB='missing_categories'
_AA='auto_mode'
_A9='created_category'
_A8='*.spriteLib'
_A7='Sprite Library'
_A6='Select Sprite Library asset'
_A5='normal'
_A4='disabled'
_A3='sprites:'
_A2='guid:'
_A1='label_to_meta'
_A0='start'
_z='mode'
_y='No categories found in the Sprite Library.'
_x='name:'
_w='DONE'
_v='Invalid input'
_u='.jpeg'
_t='.jpg'
_s='m_SpriteOverride'
_r='m_Sprite'
_q='\n'
_p='missing_sprites'
_o='missing_meta'
_n='missing_files'
_m='sprites_added'
_l='missing_source_sprites'
_k='missing_labels'
_j='missing_atlas'
_i='updated'
_h='Please select a valid .spriteLib file.'
_g='Missing Sprite Library'
_f='Run'
_e='Please choose a valid folder.'
_d='No folder'
_c='Error'
_b='label_to_sprite'
_a='categories_processed'
_Z='*.*'
_Y='All files'
_X='m_Sprite:'
_W='m_SpriteOverride:'
_V='end'
_U='.png'
_T='m_OverrideEntries:'
_S='name_to_id'
_R='    - m_Name: '
_Q='m_Library:'
_P='Running'
_O='Idle'
_N='guid'
_M='  - m_Name: '
_L='RGBA'
_K='we'
_J='Browse'
_I='.meta'
_H='replace'
_G='r'
_F='utf-8'
_E=':'
_D='w'
_C=True
_B=False
_A=None
import os,shutil,threading,re,zlib
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
import tkinter as tk
from tkinter import ttk,filedialog,messagebox
from PIL import Image
A=2048
B=4
EXT={_U,_t,_u}
ATLAS='atlas.png'
cc=os.cpu_count()or 4
IO_WORKERS=min(32,cc*2)
IMG_WORKERS=min(32,cc)
def walk(root,exts):
        B=exts;B={A.lower()for A in B};C=[root]
        while C:
                D=C.pop()
                try:
                        with os.scandir(D)as E:
                                for A in E:
                                        if A.is_dir(follow_symlinks=_B):C.append(A.path)
                                        elif A.is_file(follow_symlinks=_B):
                                                F=os.path.splitext(A.name)[1].lower()
                                                if F in B:yield A.path
                except Exception:continue
def out(p,i,s):A,B=os.path.splitext(p);return p if i else A+(s or'')+B


class CommonMixin:
    def __init__(A,r):
            A.r=r;r.title('Esperanza Tools Hub');r.geometry('1250x420');A._img_workers=IMG_WORKERS;A._io_workers=IO_WORKERS;A._style();A.n=ttk.Notebook(r);A.n.pack(fill='both',expand=1,padx=10,pady=10)
            for B in(A._comp,A._desat,A._dup,A._crunch,A._atlas,A._slices,A._spritelib,A._spritelib_overwrite,A._spritelib_renumber,A._gif,A._rename_files,A._rename_atlas):B()
    def _style(G):
            F='TNotebook.Tab';E='#2a2a2a';C='#e6e6e6';B='#1e1e1e';D=G.r;D.configure(bg=B);D.option_add('*Background',B);D.option_add('*Foreground',C);A=ttk.Style(D)
            try:A.theme_use('clam')
            except Exception:pass
            A.configure('.',background=B,foreground=C);A.configure('TFrame',background=B);A.configure('TNotebook',background=B);A.configure(F,background=E,foreground=C,padding=[10,6]);A.map(F,background=[('selected','#3a3a3a')]);A.configure('TEntry',fieldbackground=E,foreground=C);A.configure('TButton',background=E,foreground=C);A.configure('Horizontal.TProgressbar',background='#4a4a4a',troughcolor=E)
    def _tab(B,t):A=ttk.Frame(B.n);B.n.add(A,text=t);A.grid_columnconfigure(1,weight=1);return A
    def _pick(B,v):
            A=filedialog.askdirectory()
            if A:v.set(A)
    def _pick_file(B,v,filetypes):
            A=filedialog.askopenfilename(filetypes=filetypes)
            if A:v.set(A)
    def _entry_with_button(B,parent,row,var,label=None,entry_width=50,button_text=_J,button_cmd=None,entry_attr=None,button_attr=None):
            if label:
                    ttk.Label(parent,text=label).grid(row=row,column=0,sticky=_D)
            entry=ttk.Entry(parent,textvariable=var,width=entry_width)
            entry.grid(row=row,column=1,sticky=_K)
            if entry_attr is not None:
                    setattr(B,entry_attr,entry)
            cmd=button_cmd
            if cmd is None:
                    cmd=lambda v=var:B._pick(v)
            button=ttk.Button(parent,text=button_text,command=cmd)
            button.grid(row=row,column=2)
            if button_attr is not None:
                    setattr(B,button_attr,button)
    def _run(A,start,work,end):
            def B():
                    try:
                            C=work();A.r.after(0,lambda C=C:end(C))
                    except Exception as C:
                            D=str(C);print(f"[Tools Hub] Worker failed: {D}");A.r.after(0,lambda D=D:messagebox.showerror(_c,D))
            start();threading.Thread(target=B,daemon=1).start()
    def _parallel_for_each(C,items,func,workers):
            A=items
            if not A:return
            if len(A)==1:func(A[0]);return
            with ThreadPoolExecutor(max_workers=workers)as B:
                    for D in B.map(func,A):0
    def _parallel_map(C,items,func,workers):
            A=items
            if not A:return[]
            if len(A)==1:return[func(A[0])]
            with ThreadPoolExecutor(max_workers=workers)as B:return list(B.map(func,A))
    def _comp_cell_xy(A,index,grid,tile_size):
            B=index%grid;C=index//grid;return B,C,B*tile_size,(grid-1-C)*tile_size
    def _comp_sort_key(A,name,prefix):
            B=os.path.splitext(name)[0];C=B[len(prefix):]if B.startswith(prefix)else B;D=re.search('(\\d+)$',C)
            if D:E=D.group(1);return 0,int(E),len(E),B.lower()
            print(f"[10x10 Compositor] No trailing frame number found for {name}; using lexical fallback.");return 1,0,0,B.lower()
    def _nat_key(A,name):
            B=[]
            for C in re.split('(\\d+)',os.path.splitext(os.path.basename(name))[0]):
                    if C=='':continue
                    if C.isdigit():B.append((0,int(C),len(C)))
                    else:B.append((1,C.lower()))
            return B
    def _gif_frame_paths(A,folder):
            B=[]
            with os.scandir(folder)as C:
                    for D in C:
                            if not D.is_file():continue
                            E=os.path.splitext(D.name)[1].lower()
                            if E in EXT:B.append(D.path)
            B.sort(key=A._nat_key);print(f"[Folder To GIF] folder={folder} frames_found={len(B)} frames={[os.path.basename(C)for C in B[:5]]}");return B
    def _gif_output_path(A,folder):
            B=os.path.basename(os.path.normpath(folder))or'output';return os.path.join(folder,f"{B}.gif")
    def _gif_prepare_frames(A,paths):
            B=[];C=D=1
            for E in paths:
                    with Image.open(E)as F:G=F.convert(_L);B.append(G.copy());C=max(C,G.width);D=max(D,G.height)
            if any(E.size!=(C,D)for E in B):
                    print(f"[Folder To GIF] Normalizing frame sizes to {C}x{D}.")
                    E=[]
                    for F in B:
                            G=Image.new(_L,(C,D),(0,0,0,0));H=((C-F.width)//2,(D-F.height)//2);G.paste(F,H,F);E.append(G);F.close()
                    B=E
            return B,C,D


__all__ = [
    '_AY', '_AX', '_AW', '_AV', '_AU', '_AT', '_AS', '_AR',
    '_AQ', '_AP', '_AO', '_AN', '_AM', '_AL', '_AK', '_AJ',
    '_AI', '_AH', '_AG', '_AF', '_AE', '_AD', '_AC', '_AB',
    '_AA', '_A9', '_A8', '_A7', '_A6', '_A5', '_A4', '_A3',
    '_A2', '_A1', '_A0', '_z', '_y', '_x', '_w', '_v',
    '_u', '_t', '_s', '_r', '_q', '_p', '_o', '_n',
    '_m', '_l', '_k', '_j', '_i', '_h', '_g', '_f',
    '_e', '_d', '_c', '_b', '_a', '_Z', '_Y', '_X',
    '_W', '_V', '_U', '_T', '_S', '_R', '_Q', '_P',
    '_O', '_N', '_M', '_L', '_K', '_J', '_I', '_H',
    '_G', '_F', '_E', '_D', '_C', '_B', '_A',
    'os', 'shutil', 'threading', 're', 'zlib', 'Path',
    'ThreadPoolExecutor', 'tk', 'ttk', 'filedialog', 'messagebox',
    'Image', 'A', 'B', 'EXT', 'ATLAS', 'cc', 'IO_WORKERS',
    'IMG_WORKERS', 'walk', 'out', 'CommonMixin',
]
