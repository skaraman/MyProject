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
class AA:
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
        def _gif(A):
                B=A._tab('Folder To GIF');A.gif_folder=tk.StringVar(A.r);A.gif_duration=tk.StringVar(A.r,'100');A.gif_status=tk.StringVar(A.r,_O);C=0;A._entry_with_button(B,C,A.gif_folder,label='Folder:');C+=1;ttk.Label(B,text='Frame delay (ms):').grid(row=C,column=0,sticky=_D);ttk.Entry(B,textvariable=A.gif_duration,width=10).grid(row=C,column=1,sticky=_D);C+=1;ttk.Label(B,text='Builds one GIF beside the selected frames folder using natural numeric order.').grid(row=C,column=0,columnspan=3,sticky=_D);C+=1
                def D():
                        B=A.gif_folder.get().strip()
                        try:C=max(1,int(A.gif_duration.get().strip()))
                        except ValueError:messagebox.showerror(_v,'Frame delay must be an integer.');return
                        if not B or not os.path.isdir(B):messagebox.showerror(_d,_e);return
                        def D():
                                D=A._gif_frame_paths(B)
                                if not D:raise ValueError('No PNG/JPG/JPEG files found in the selected folder.')
                                E=A._gif_output_path(B);F,G,H=A._gif_prepare_frames(D)
                                try:
                                        I,*J=F;K=I.convert('P',palette=Image.ADAPTIVE)
                                        L=[M.convert('P',palette=Image.ADAPTIVE)for M in J]
                                        print(f"[Folder To GIF] output={E} frame_count={len(F)} duration_ms={C} size={G}x{H}")
                                        K.save(E,save_all=_C,append_images=L,duration=C,loop=0,disposal=2)
                                finally:
                                        for M in F:M.close()
                                        if 'L'in locals():
                                                for M in L:M.close()
                                        if 'K'in locals():K.close()
                                return E,len(D),C
                        def E(result):
                                B,C,D=result;A.gif_status.set(f"DONE - wrote {C} frame(s) to {B} at {D}ms per frame.")
                        A._run(lambda:A.gif_status.set(_P),D,E)
                ttk.Button(B,text=_f,command=D).grid(row=C,column=0,columnspan=3);C+=1;ttk.Label(B,textvariable=A.gif_status).grid(row=C,column=0,columnspan=3,sticky=_D)
        def _comp(A):
                B=A._tab('10x10 Compositor');A.comp_folder=tk.StringVar(A.r);A.comp_prefix=tk.StringVar(A.r,'frame_');A.comp_canvas=tk.StringVar(A.r,'1920');A.comp_grid=tk.StringVar(A.r,'10');A.comp_status=tk.StringVar(A.r,_O)
                for(C,(D,E,F))in enumerate((('Root:',A.comp_folder,50),('Prefix:',A.comp_prefix,25),('Canvas:',A.comp_canvas,10),('Grid:',A.comp_grid,10))):
                        ttk.Label(B,text=D).grid(row=C,column=0,sticky=_D);ttk.Entry(B,textvariable=E,width=F).grid(row=C,column=1,sticky=_D)
                        if C==0:ttk.Button(B,text=_J,command=lambda:A._pick(A.comp_folder)).grid(row=C,column=2)
                def G():
                        C=A.comp_folder.get().strip();D=A.comp_prefix.get().strip()
                        try:E=int(A.comp_canvas.get());B=int(A.comp_grid.get())
                        except ValueError:messagebox.showerror(_v,'Canvas size and Grid size must be integers.');return
                        if E<=0 or B<=0:messagebox.showerror(_v,'Canvas size and Grid size must be greater than zero.');return
                        if E//B<=0:messagebox.showerror(_v,'Canvas size must be >= Grid size.');return
                        if not C or not os.path.isdir(C):messagebox.showerror(_d,_e);return
                        if not D:messagebox.showerror('No prefix','Please enter the filename prefix.');return
                        def F():
                                F=[]
                                for(I,K,G)in os.walk(C):
                                        H=[A for A in G if A.endswith(_U)and D in A]
                                        if H:F.append((I,H,G))
                                def J(job):
                                        K,H,P=job;H=sorted(H,key=lambda C:A._comp_sort_key(C,D));A._clear_comp_sprite_meta(K,P);R=E//B;Q=B*B;C=Image.new(_L,(E,E));L=0;N=1
                                        for J in H:
                                                V=os.path.join(K,J)
                                                F,I,S,T=A._comp_cell_xy(L,B,R)
                                                if F==0:print(f"[10x10 Compositor] {K} sheet={N} row_from_bottom={I} starts with {J} at ({S},{T})")
                                                with Image.open(V)as U:G=U.resize((R,R))
                                                if G.mode in(_L,'LA'):C.paste(G,(S,T),G)
                                                elif _AF in G.info:M=G.convert(_L);C.paste(M,(S,T),M);M.close()
                                                else:C.paste(G,(S,T))
                                                G.close();L+=1
                                                if L==Q or J==H[-1]:
                                                        V=os.path.join(K,f"{N}.png");print(f"[10x10 Compositor] Saving {V} with {L} frame(s).");C.save(V);C.close();C=_A;N+=1
                                                        if J!=H[-1]:C=Image.new(_L,(E,E));L=0
                                        if C is not _A:C.close()
                                        for J in H:os.remove(os.path.join(K,J))
                                A._parallel_for_each(F,J,A._img_workers)
                        A._run(lambda:A.comp_status.set(_P),F,lambda _:A.comp_status.set(_w))
                ttk.Button(B,text=_f,command=G).grid(row=4,column=0,columnspan=3);ttk.Label(B,textvariable=A.comp_status).grid(row=5,column=0,columnspan=3,sticky=_D)
        def _clear_sprite_meta_slices(K,meta_path):
                L=meta_path
                try:
                        with open(L,_G,encoding=_F,errors=_H,newline='')as G:F=G.readlines()
                except Exception:return _B
                def H(start_index,base_indent,include_dash):
                        C=base_indent;A=start_index
                        while A<len(F):
                                B,G=K._split_line_ending(F[A]);D=B.strip()
                                if D=='':A+=1;continue
                                E=len(B)-len(B.lstrip())
                                if E>C:A+=1;continue
                                if include_dash and E==C and D.startswith('-'):A+=1;continue
                                break
                        return A
                D=_B;E=[];A=0
                while A<len(F):
                        M=F[A];B,I=K._split_line_ending(M);J=B.strip();C=len(B)-len(B.lstrip())
                        if J.startswith(_AQ):E.append(f"{B[:C]}internalIDToNameTable: []{I}");D=_C;A=H(A+1,C,_C);continue
                        if J.startswith(_A3):E.append(f"{B[:C]}sprites: []{I}");D=_C;A=H(A+1,C,_C);continue
                        if J.startswith(_AG):E.append(f"{B[:C]}nameFileIdTable: {{}}{I}");D=_C;A=H(A+1,C,_B);continue
                        E.append(M);A+=1
                if D:
                        with open(L,_D,encoding=_F,newline='')as G:G.writelines(E)
                return D
        def _clear_comp_sprite_meta(C,root,files):
                B='.png.meta'
                for A in files:
                        D=A.lower()
                        if not D.endswith(B):continue
                        E=A[:-len(B)]
                        if E.isdigit():C._clear_sprite_meta_slices(os.path.join(root,A))
        def _desat(A):
                B=A._tab('Desaturate PNGs');A.desat_folder=tk.StringVar(A.r);A.desat_status=tk.StringVar(A.r,_O);A._entry_with_button(B,0,A.desat_folder)
                def C():
                        B=A.desat_folder.get().strip()
                        if not B or not os.path.isdir(B):messagebox.showerror(_d,_e);return
                        def C():
                                C=list(walk(B,[_U]))
                                def D(p):
                                        with Image.open(p)as F:B=F.convert(_L)
                                        C,C,C,D=B.split();A=B.convert('L');E=Image.merge(_L,(A,A,A,D));E.save(p);E.close();B.close();A.close();D.close()
                                A._parallel_for_each(C,D,A._img_workers)
                        A._run(lambda:A.desat_status.set(_P),C,lambda _:A.desat_status.set(_w))
                ttk.Button(B,text=_f,command=C).grid(row=1,column=0,columnspan=3);ttk.Label(B,textvariable=A.desat_status).grid(row=2,column=0,columnspan=3)
        def _dup(A):
                B=A._tab('Dup+Merge');A.dup_folder=tk.StringVar(A.r);A.dup_passes=tk.StringVar(A.r,'1');A.dup_inplace=tk.BooleanVar(A.r,_C);A.dup_suffix=tk.StringVar(A.r,'_merged');A.dup_status=tk.StringVar(A.r,_O);A._entry_with_button(B,0,A.dup_folder);ttk.Label(B,text='Passes:').grid(row=1,column=0,sticky=_D);ttk.Entry(B,textvariable=A.dup_passes,width=10).grid(row=1,column=1,sticky=_D);ttk.Checkbutton(B,text='Overwrite files (in-place)',variable=A.dup_inplace).grid(row=2,column=0,sticky=_D);ttk.Label(B,text='Output suffix (only if not in-place):').grid(row=3,column=0,sticky=_D);ttk.Entry(B,textvariable=A.dup_suffix,width=15).grid(row=3,column=1,sticky=_D)
                def C():
                        B=A.dup_folder.get().strip()
                        if not B or not os.path.isdir(B):messagebox.showerror(_d,_e);return
                        try:C=int(A.dup_passes.get())
                        except ValueError:messagebox.showerror(_v,'Passes must be an integer.');return
                        if C<0:messagebox.showerror(_v,'Passes must be zero or greater.');return
                        D=A.dup_inplace.get();E=A.dup_suffix.get()
                        def F():
                                F=list(walk(B,[_U]))
                                def G(p):
                                        with Image.open(p)as F:B=F.convert(_L)
                                        A=B
                                        for H in range(C):
                                                G=Image.alpha_composite(A,A)
                                                if A is not B:A.close()
                                                A=G
                                        A.save(out(p,D,E));B.close()
                                        if A is not B:A.close()
                                A._parallel_for_each(F,G,A._img_workers)
                        A._run(lambda:A.dup_status.set(_P),F,lambda _:A.dup_status.set(_w))
                ttk.Button(B,text=_f,command=C).grid(row=4,column=0,columnspan=3);ttk.Label(B,textvariable=A.dup_status).grid(row=5,column=0,columnspan=3)
        def _crunch(C):
                D=C._tab('4x4 Crunch');C.crunch_folder=tk.StringVar(C.r);C.crunch_status=tk.StringVar(C.r,_O);C._entry_with_button(D,0,C.crunch_folder)
                def E():
                        D=C.crunch_folder.get().strip()
                        if not D or not os.path.isdir(D):messagebox.showerror(_d,_e);return
                        def E():
                                E=list(walk(D,EXT))
                                def F(p):
                                        with Image.open(p)as C:
                                                E,F=C.size
                                                if E>A or F>A:return
                                                G=(E+2+B-1)//B*B;H=(F+2+B-1)//B*B
                                                if G==E and H==F:return
                                                if G>A or H>A:return
                                                D=Image.new(_L,(G,H))
                                                if C.mode in(_L,'LA')or _AF in C.info:I=C.convert(_L);D.paste(I,(1,1),I);I.close()
                                                else:D.paste(C,(1,1))
                                                D.save(p);D.close()
                                C._parallel_for_each(E,F,C._img_workers)
                        C._run(lambda:C.crunch_status.set(_P),E,lambda _:C.crunch_status.set(_w))
                ttk.Button(D,text=_f,command=E).grid(row=1,column=0,columnspan=3);ttk.Label(D,textvariable=C.crunch_status).grid(row=2,column=0,columnspan=3)
        def _atlas_output_path(B,root,index):
                C,D=os.path.splitext(ATLAS);return os.path.join(root,ATLAS if index==0 else f"{C}{index}{D}")
        def _atlas_source_files(B,root):
                C,D=os.path.splitext(ATLAS);E=re.compile(f"^{re.escape(C)}(\\d+)?{re.escape(D)}$",re.I);A=[]
                for F in walk(root,EXT):
                        if E.match(os.path.basename(F)):continue
                        A.append(F)
                return sorted(A,key=lambda A:A.lower())
        def _trim_packed_atlas_page(B,image,used_width,used_height):
                C=max(1,used_width);D=max(1,used_height)
                if image.width==C and image.height==D:return image
                E=image.crop((0,0,C,D));image.close();return E
        def _build_root_atlas_pages(B,root,image_paths):
                E=[];F=Image.new(_L,(A,A));C=D=H=I=N=0
                def J(final_page=_B):
                        nonlocal F,C,D,H,I,N
                        L=max(1,N);M=max(1,D+H);K=B._atlas_output_path(root,I);print(f"[Atlas 2048] Saving {K} with page_index={I}, size={L}x{M}, cursor_x={C}, row_y={D}, row_h={H}.");O=B._trim_packed_atlas_page(F,L,M);O.save(K);O.close();E.append(K);I+=1
                        if not final_page:F=Image.new(_L,(A,A));C=D=H=N=0
                try:
                        for K in image_paths:
                                with Image.open(K)as L:
                                        if L.width>A or L.height>A:raise ValueError(f"Image is larger than {A}x{A} and cannot fit in an atlas:\n{K}")
                                        if C+L.width>A:C=0;D+=H;H=0
                                        if D+L.height>A:J();C=D=H=0
                                        if L.mode in(_L,'LA')or _AF in L.info:M=L.convert(_L);F.paste(M,(C,D),M);M.close()
                                        else:F.paste(L,(C,D))
                                        C+=L.width;H=max(H,L.height);N=max(N,C)
                        if image_paths:J(_C)
                        else:F.close()
                        return E
                except Exception:
                        F.close();raise
        def _cleanup_root_atlas_outputs(B,root,keep_paths):
                C,D=os.path.splitext(ATLAS);E=re.compile(f"^{re.escape(C)}(\\d+)?{re.escape(D)}$",re.I);F={os.path.normcase(os.path.normpath(A))for A in keep_paths};G=0
                for H in os.listdir(root):
                        I=os.path.join(root,H)
                        if not os.path.isfile(I)or not E.match(H):continue
                        if os.path.normcase(os.path.normpath(I))in F:continue
                        print(f"[Atlas 2048] Removing stale atlas {I}.");os.remove(I);G+=1;J=I+_I
                        if os.path.isfile(J):os.remove(J)
                return G
        def _delete_flattened_atlas_sources(B,root,image_paths):
                C=os.path.normcase(os.path.normpath(root));D=0;E=0
                for F in image_paths:
                        G=os.path.normcase(os.path.normpath(os.path.dirname(F)))
                        if G!=C:continue
                        if os.path.isfile(F):os.remove(F);D+=1
                        H=F+_I
                        if os.path.isfile(H):os.remove(H)
                for F in os.listdir(root):
                        G=os.path.join(root,F)
                        if not os.path.isdir(G):continue
                        print(f"[Atlas 2048] Removing source folder {G}.");shutil.rmtree(G);E+=1;H=G+_I
                        if os.path.isfile(H):os.remove(H)
                return D,E
        def _atlas(B):
                C=B._tab('Atlas 2048');B.atlas_folder=tk.StringVar(B.r);B.atlas_status=tk.StringVar(B.r,_O);B._entry_with_button(C,0,B.atlas_folder)
                def D():
                        C=B.atlas_folder.get().strip()
                        if not C or not os.path.isdir(C):messagebox.showerror(_d,_e);return
                        def D():
                                D=B._atlas_source_files(C)
                                if not D:return 0,0,0,0
                                E=B._build_root_atlas_pages(C,D);F=B._cleanup_root_atlas_outputs(C,E);G,H=B._delete_flattened_atlas_sources(C,D);return len(E),len(D),H,F+G
                        def E(result):
                                A,C,D,E=result
                                if not C:B.atlas_status.set('DONE - No images found.');return
                                B.atlas_status.set(f"DONE - {A} atlas(es), {C} image(s), {D} folder(s) removed, {E} root file(s) removed.")
                        B._run(lambda:B.atlas_status.set(_P),D,E)
                ttk.Button(C,text='Build Atlases',command=D).grid(row=1,column=0,columnspan=3);ttk.Label(C,textvariable=B.atlas_status).grid(row=2,column=0,columnspan=3)
        def _slices(A):
                B=A._tab('Copy Sprite Slices');A.slices_root=tk.StringVar(A.r);A.slices_status=tk.StringVar(A.r,_O);A._entry_with_button(B,0,A.slices_root)
                def C():
                        E=A.slices_root.get().strip()
                        if not E or not os.path.isdir(E):messagebox.showerror(_AR,'Please select a valid root folder.');return
                        F,B,C=A._collect_slices_pairs(E)
                        if not F:
                                D='No JPG targets matched PNG names.'
                                if C>0 and B>0:D=f"No copies made. Missing .meta for {C} PNG file(s). Skipped {B} PNG file(s) without matches."
                                elif C>0:D=f"No copies made. Missing .meta for {C} PNG file(s)."
                                elif B>0:D=f"No copies made. Skipped {B} PNG file(s) without matches."
                                A.slices_status.set(D);return
                        G=sum(1 for(B,A)in F if os.path.isfile(A))
                        if G:A.slices_status.set(f"Overwriting {G} existing .meta file(s).")
                        def H():
                                def G(pair):
                                        A,B=pair
                                        try:shutil.copyfile(A,B);return _C
                                        except Exception:return _B
                                D=A._parallel_map(F,G,A._io_workers);E=sum(1 for A in D if A);H=len(D)-E;return E,H,B,C
                        def I(result):
                                D,B,E,F=result;C=f"DONE - copied slices to {D} JPG file(s)."
                                if B:C=f"DONE - copied slices to {D} JPG file(s), {B} failed."
                                if E:C+=f" Skipped {E} PNG file(s) without matches."
                                if F:C+=f" {F} PNG file(s) missing .meta."
                                A.slices_status.set(C)
                                if B:messagebox.showwarning('Copy issues',f"Failed to copy {B} .meta file(s).")
                        A._run(lambda:A.slices_status.set(_P),H,I)
                ttk.Button(B,text='Copy',command=C).grid(row=1,column=0,columnspan=3);ttk.Label(B,textvariable=A.slices_status).grid(row=2,column=0,columnspan=3)
        def _collect_slices_pairs(a,root_folder):
                K=[];F=0;L=set();U={_t,_u}
                for(M,b,N)in os.walk(root_folder):
                        V={A.lower()for A in N};D=[];G=[]
                        for H in N:
                                O=os.path.splitext(H)[1].lower()
                                if O==_U:D.append(H)
                                elif O in U:G.append(H)
                        if not D:continue
                        if not G:F+=len(D);continue
                        E={};P={};Q={}
                        for I in D:
                                W=os.path.splitext(I)[0];A=W.lower()
                                if A in E:continue
                                E[A]=I;R=f"{I}.meta";C=os.path.join(M,R)
                                if R.lower()in V:P[A]=C
                                else:Q[A]=C
                        X=sorted(E.keys(),key=len,reverse=_C);S=set()
                        for T in G:
                                Y=os.path.splitext(T)[0];J=Y.lower();B=_A
                                for A in X:
                                        if J==A:B=A;break
                                        if J.startswith(A)and J[len(A):].isdigit():B=A;break
                                if not B:continue
                                S.add(B);C=P.get(B)
                                if not C:L.add(Q[B]);continue
                                Z=os.path.join(M,T)+_I;K.append((C,Z))
                        F+=max(0,len(E)-len(S))
                return K,F,len(L)
        def _rename_files(A):
                C=A._tab('Rename Files');A.rename_folder=tk.StringVar(A.r);A.rename_match=tk.StringVar(A.r);A.rename_replace=tk.StringVar(A.r);A.rename_status=tk.StringVar(A.r,_O);B=0;A._entry_with_button(C,B,A.rename_folder,label='Folder:');B+=1;ttk.Label(C,text='Exact name to match (no extension):').grid(row=B,column=0,sticky=_D);ttk.Entry(C,textvariable=A.rename_match,width=30).grid(row=B,column=1,sticky=_D);B+=1;ttk.Label(C,text='Replacement name (no extension):').grid(row=B,column=0,sticky=_D);ttk.Entry(C,textvariable=A.rename_replace,width=30).grid(row=B,column=1,sticky=_D);B+=1;ttk.Label(C,text='Matches file name without extension; keeps the extension.').grid(row=B,column=0,columnspan=3,sticky=_D);B+=1
                def D():
                        F='Missing name';D=A.rename_folder.get().strip();B=A.rename_match.get().strip();C=A.rename_replace.get().strip()
                        if not D or not os.path.isdir(D):messagebox.showerror(_d,_e);return
                        if not B:messagebox.showerror(F,'Please enter the exact name to match.');return
                        if not C:messagebox.showerror(F,'Please enter the replacement name.');return
                        if B==C:messagebox.showerror('No change','Match name and replacement name are the same.');return
                        for E in(B,C):
                                if os.path.sep in E or os.path.altsep and os.path.altsep in E:messagebox.showerror('Invalid name','Names must not include path separators.');return
                        def G():
                                K='failed';J='renamed';I='conflict';F=[]
                                for(G,U,L)in os.walk(D):
                                        for H in L:
                                                M,N=os.path.splitext(H)
                                                if M!=B:continue
                                                O=os.path.join(G,H);P=os.path.join(G,C+N);F.append((O,P))
                                def Q(item):
                                        B,A=item
                                        if os.path.exists(A)and os.path.normcase(B)!=os.path.normcase(A):return I
                                        try:os.rename(B,A);return J
                                        except Exception:return K
                                E=A._parallel_map(F,Q,A._io_workers);R=sum(1 for A in E if A==J);S=sum(1 for A in E if A==I);T=sum(1 for A in E if A==K);return R,S,T
                        def H(result):
                                E,C,D=result;B=f"DONE - renamed {E} file(s)."
                                if C:B+=f" Skipped {C} existing target name(s)."
                                if D:B+=f" {D} failed."
                                A.rename_status.set(B)
                        A._run(lambda:A.rename_status.set(_P),G,H)
                ttk.Button(C,text=_f,command=D).grid(row=B,column=0,columnspan=3);B+=1;ttk.Label(C,textvariable=A.rename_status).grid(row=B,column=0,columnspan=3,sticky=_D)
        def _rename_atlas(A):
                C=A._tab('Rename Atlas');A.atlas_rename_file=tk.StringVar(A.r);A.atlas_rename_prefix=tk.StringVar(A.r);A.atlas_rename_suffix=tk.StringVar(A.r);A.atlas_rename_status=tk.StringVar(A.r,_O);B=0;A._entry_with_button(C,B,A.atlas_rename_file,label='Sprite atlas file:',button_cmd=lambda:A._pick_file(A.atlas_rename_file,[('Sprite atlases',('*.png','*.jpg','*.jpeg')),(_Y,_Z)]));B+=1;ttk.Label(C,text='Prefix:').grid(row=B,column=0,sticky=_D);ttk.Entry(C,textvariable=A.atlas_rename_prefix,width=30).grid(row=B,column=1,sticky=_D);B+=1;ttk.Label(C,text='Suffix:').grid(row=B,column=0,sticky=_D);ttk.Entry(C,textvariable=A.atlas_rename_suffix,width=30).grid(row=B,column=1,sticky=_D);B+=1;ttk.Label(C,text='New names will be prefix + number + suffix. Leave prefix/suffix blank for just numbers.').grid(row=B,column=0,columnspan=3,sticky=_D);B+=1
                def D():
                        B=A.atlas_rename_file.get().strip();H=A.atlas_rename_prefix.get();I=A.atlas_rename_suffix.get()
                        if not B or not os.path.isfile(B):messagebox.showerror(_AS,'Please select a valid sprite atlas file.');return
                        C=B+_I
                        if not os.path.isfile(C):messagebox.showerror('Missing meta','Atlas .meta file not found.');return
                        def D():
                                with open(C,_G,encoding=_F,errors=_H,newline='')as B:D=B.readlines()
                                E=A._rename_sprite_sheet_entries(D,H,I);F=len(E);G=0
                                if F:
                                        G=A._rename_name_file_id_table(D,E)
                                        with open(C,_D,encoding=_F,newline='')as B:B.writelines(D)
                                return F,G
                        def E(result):
                                B,C=result
                                if B:
                                        D=f"DONE - renamed {B} sprite(s)."
                                        if C and C!=B:D+=f" Updated {C} nameFileIdTable entries."
                                else:D='No sprites renamed (spriteSheet section missing or empty).'
                                A.atlas_rename_status.set(D)
                        A._run(lambda:A.atlas_rename_status.set(_P),D,E)
                ttk.Button(C,text=_f,command=D).grid(row=B,column=0,columnspan=3);B+=1;ttk.Label(C,textvariable=A.atlas_rename_status).grid(row=B,column=0,columnspan=3,sticky=_D)
        def _rename_sprite_sheet_name_line(A,line,new_name):
                B,C=A._split_line_ending(line)
                if _x not in B:return line
                D,E=B.split(_x,1);F=A._format_sprite_label(new_name,E.strip());return f"{D}name: {F}{C}"
        def _rename_sprite_sheet_entries(K,lines,prefix,suffix):
                H=suffix;G=prefix;F=lines;G=G or'';H=H or'';L={};I=_B;M=0;B=_B;J=0;A=_B;N=1
                for(O,D)in enumerate(F):
                        C=D.strip()
                        if C.startswith(_AH):I=_C;M=len(D)-len(D.lstrip());B=_B;A=_B;continue
                        if not I:continue
                        if C=='':continue
                        E=len(D)-len(D.lstrip())
                        if E<=M:I=_B;B=_B;A=_B;continue
                        if C.startswith(_A3):B=_C;J=E;A=_B;continue
                        if not B:continue
                        if E<J:B=_B;A=_B;continue
                        if E==J:
                                if C.startswith('-'):A=_C;continue
                                B=_B;A=_B;continue
                        if not A:continue
                        if C.startswith(_x):P=C.split(_E,1)[1].strip();Q=K._normalize_entry_name(P);R=f"{G}{N}{H}";F[O]=K._rename_sprite_sheet_name_line(F[O],R);S=Q if Q else P;L[S]=R;N+=1
                return L
        def _rename_name_file_id_table(C,lines,name_map):
                D=lines;E=_B;F=0;G=0
                for(L,A)in enumerate(D):
                        H=A.strip()
                        if H.startswith(_AG):E=_C;F=len(A)-len(A.lstrip());continue
                        if not E:continue
                        if H=='':continue
                        B=len(A)-len(A.lstrip())
                        if B<=F:break
                        I=A[B:]
                        if _E not in I:continue
                        J,M=I.split(_E,1);N=C._normalize_entry_name(J.strip());K=name_map.get(N)
                        if not K:continue
                        O=C._format_sprite_label(K,J.strip());D[L]=f"{A[:B]}{O}:{M}";G+=1
                return G
        def _spritelib(A):C=A._tab('Sprite Library Replace');A.sprite_lib_source_path=tk.StringVar(A.r);A.sprite_lib_target_path=tk.StringVar(A.r);A.sprite_lib_category=tk.StringVar(A.r);A.sprite_lib_atlas=tk.StringVar(A.r);A.sprite_lib_auto_find=tk.BooleanVar(A.r,_B);A.sprite_lib_auto_jpg=tk.BooleanVar(A.r,_C);A.sprite_lib_auto_folder=tk.StringVar(A.r);A.sprite_lib_status=tk.StringVar(A.r,_O);B=0;A._entry_with_button(C,B,A.sprite_lib_source_path,label='Source Sprite Library asset (.spriteLib):',button_cmd=A._pick_sprite_library_source);B+=1;A._entry_with_button(C,B,A.sprite_lib_target_path,label='Target Sprite Library asset (.spriteLib):',button_cmd=A._pick_sprite_library_target);B+=1;ttk.Label(C,text='Category to replace (manual):').grid(row=B,column=0,sticky=_D);A.sprite_lib_category_combo=ttk.Combobox(C,textvariable=A.sprite_lib_category,width=28);A.sprite_lib_category_combo.grid(row=B,column=1,sticky=_D);A.sprite_lib_category_button=ttk.Button(C,text=_AI,command=A._load_sprite_library_categories);A.sprite_lib_category_button.grid(row=B,column=2);B+=1;A._entry_with_button(C,B,A.sprite_lib_atlas,label='Atlas image (manual PNG/JPG):',button_cmd=A._pick_sprite_library_atlas,entry_attr='sprite_lib_atlas_entry',button_attr='sprite_lib_atlas_button');B+=1;A.sprite_lib_auto_jpg_check=ttk.Checkbutton(C,text='Use JPG atlases (unchecked = PNG)',variable=A.sprite_lib_auto_jpg);A.sprite_lib_auto_jpg_check.grid(row=B,column=0,columnspan=3,sticky=_D);B+=1;A.sprite_lib_auto_find_check=ttk.Checkbutton(C,text='Auto-find atlas per category (folder)',variable=A.sprite_lib_auto_find,command=A._toggle_sprite_lib_auto_find);A.sprite_lib_auto_find_check.grid(row=B,column=0,columnspan=3,sticky=_D);B+=1;A._entry_with_button(C,B,A.sprite_lib_auto_folder,label='Atlas folder (auto):',button_cmd=lambda:A._pick(A.sprite_lib_auto_folder),entry_attr='sprite_lib_auto_folder_entry',button_attr='sprite_lib_auto_folder_button');B+=1;ttk.Label(C,text='Manual mode uses a single atlas .meta for one category.\nAuto-find scans a folder and processes all categories.').grid(row=B,column=0,columnspan=3,sticky=_D);B+=1;ttk.Button(C,text='Replace Category Sprites',command=A._start_replace_sprite_library).grid(row=B,column=0,columnspan=3);B+=1;ttk.Label(C,textvariable=A.sprite_lib_status).grid(row=B,column=0,columnspan=3,sticky=_D);A._toggle_sprite_lib_auto_find()
        def _toggle_sprite_lib_auto_find(B):
                D=B.sprite_lib_auto_find.get();E=_A4 if D else _A5;F=_A5 if D else _A4
                for C in('sprite_lib_category_combo','sprite_lib_category_button','sprite_lib_atlas_entry','sprite_lib_atlas_button'):
                        A=getattr(B,C,_A)
                        if A:A.configure(state=E)
                for C in('sprite_lib_auto_folder_entry','sprite_lib_auto_folder_button'):
                        A=getattr(B,C,_A)
                        if A:A.configure(state=F)
        def _spritelib_overwrite(A):
                C=A._tab('Sprite Library Overwrite');A.sprite_overwrite_path=tk.StringVar(A.r);A.sprite_overwrite_category=tk.StringVar(A.r);A.sprite_overwrite_root=tk.StringVar(A.r);A.sprite_overwrite_file=tk.StringVar(A.r,'1.png');A.sprite_overwrite_target=tk.StringVar(A.r,'fL');A.sprite_overwrite_status=tk.StringVar(A.r,_O);A.sprite_overwrite_auto=tk.BooleanVar(A.r,_B);B=0;A._entry_with_button(C,B,A.sprite_overwrite_path,label=_AT,button_cmd=A._pick_sprite_library_overwrite);B+=1;ttk.Label(C,text='Target Category (optional):').grid(row=B,column=0,sticky=_D);A.sprite_overwrite_category_combo=ttk.Combobox(C,textvariable=A.sprite_overwrite_category,width=28);A.sprite_overwrite_category_combo.grid(row=B,column=1,sticky=_D);A.sprite_overwrite_category_button=ttk.Button(C,text=_AI,command=A._load_sprite_library_categories_overwrite);A.sprite_overwrite_category_button.grid(row=B,column=2);B+=1
                def D():B=_A4 if A.sprite_overwrite_auto.get()else _A5;A.sprite_overwrite_category_combo.configure(state=B);A.sprite_overwrite_category_button.configure(state=B)
                ttk.Checkbutton(C,text='Auto-match categories by folder name',variable=A.sprite_overwrite_auto,command=D).grid(row=B,column=0,columnspan=3,sticky=_D);B+=1;A._entry_with_button(C,B,A.sprite_overwrite_root,label='Scan Root Folder:',button_cmd=lambda:A._pick(A.sprite_overwrite_root));B+=1;ttk.Label(C,text='Texture File Name:').grid(row=B,column=0,sticky=_D);ttk.Entry(C,textvariable=A.sprite_overwrite_file,width=20).grid(row=B,column=1,sticky=_D);B+=1;ttk.Label(C,text='Target Folder Name:').grid(row=B,column=0,sticky=_D);ttk.Entry(C,textvariable=A.sprite_overwrite_target,width=20).grid(row=B,column=1,sticky=_D);B+=1;ttk.Label(C,text="Example: Assets/Sprites/Characters/Ana/Run/Aqua/aa/fL/1.png\nIf category is blank, the root folder name is used.\nAuto mode scans for folders that match category names.\nFile name can be '1.png' or an extension like 'png'.").grid(row=B,column=0,columnspan=3,sticky=_D);B+=1;ttk.Button(C,text='Overwrite Library with New Labels',command=A._start_overwrite_sprite_library).grid(row=B,column=0,columnspan=3);B+=1;ttk.Label(C,textvariable=A.sprite_overwrite_status).grid(row=B,column=0,columnspan=3,sticky=_D)
        def _spritelib_renumber(A):
                C=A._tab('Sprite Library Resorter')
                A.sprite_renumber_path=tk.StringVar(A.r)
                A.sprite_renumber_category=tk.StringVar(A.r)
                A.sprite_renumber_prefix=tk.StringVar(A.r)
                A.sprite_renumber_suffix=tk.StringVar(A.r)
                A.sprite_renumber_status=tk.StringVar(A.r,_O)
                A.sprite_renumber_auto=tk.BooleanVar(A.r,_B)
                A.sprite_renumber_alpha=tk.BooleanVar(A.r,_B)
                B=0
                A._entry_with_button(C,B,A.sprite_renumber_path,label=_AT,button_cmd=A._pick_sprite_library_renumber);B+=1
                ttk.Label(C,text='Category to resort:').grid(row=B,column=0,sticky=_D)
                A.sprite_renumber_category_combo=ttk.Combobox(C,textvariable=A.sprite_renumber_category,width=28)
                A.sprite_renumber_category_combo.grid(row=B,column=1,sticky=_D)
                A.sprite_renumber_category_button=ttk.Button(C,text=_AI,command=A._load_sprite_library_categories_renumber)
                A.sprite_renumber_category_button.grid(row=B,column=2);B+=1
                def D():
                        B=_A4 if A.sprite_renumber_auto.get()else _A5
                        A.sprite_renumber_category_combo.configure(state=B)
                        A.sprite_renumber_category_button.configure(state=B)
                ttk.Checkbutton(C,text='Auto: resort all categories',variable=A.sprite_renumber_auto,command=D).grid(row=B,column=0,columnspan=3,sticky=_D);B+=1
                ttk.Button(C,text='Remove non-skin labels',command=A._start_remove_non_skin_labels).grid(row=B,column=0,columnspan=3,sticky=_K);B+=1
                ttk.Label(C,text='Prefix (optional):').grid(row=B,column=0,sticky=_D)
                ttk.Entry(C,textvariable=A.sprite_renumber_prefix,width=20).grid(row=B,column=1,sticky=_D);B+=1
                ttk.Label(C,text='Suffix (optional):').grid(row=B,column=0,sticky=_D)
                ttk.Entry(C,textvariable=A.sprite_renumber_suffix,width=20).grid(row=B,column=1,sticky=_D);B+=1
                ttk.Checkbutton(C,text='Use alphabetic labels (A, B, ..., AA)',variable=A.sprite_renumber_alpha).grid(row=B,column=0,columnspan=3,sticky=_D);B+=1
                ttk.Label(C,text='Renames labels in the chosen category to prefix + number + suffix\nor prefix + letter + suffix, starting at 1 or A.').grid(row=B,column=0,columnspan=3,sticky=_D);B+=1
                ttk.Button(C,text='Resort Labels',command=A._start_renumber_sprite_library).grid(row=B,column=0,columnspan=3);B+=1
                ttk.Label(C,textvariable=A.sprite_renumber_status).grid(row=B,column=0,columnspan=3,sticky=_D)
        def _pick_sprite_library_source(A):
                B=filedialog.askopenfilename(title=_A6,filetypes=[(_A7,_A8),(_Y,_Z)])
                if B:A.sprite_lib_source_path.set(B);A._load_sprite_library_categories()
        def _pick_sprite_library_target(B):
                A=filedialog.askopenfilename(title=_A6,filetypes=[(_A7,_A8),(_Y,_Z)])
                if A:B.sprite_lib_target_path.set(A)
        def _pick_sprite_library_overwrite(A):
                B=filedialog.askopenfilename(title=_A6,filetypes=[(_A7,_A8),(_Y,_Z)])
                if B:A.sprite_overwrite_path.set(B);A._load_sprite_library_categories_overwrite()
        def _pick_sprite_library_renumber(A):
                B=filedialog.askopenfilename(title=_A6,filetypes=[(_A7,_A8),(_Y,_Z)])
                if B:A.sprite_renumber_path.set(B);A._load_sprite_library_categories_renumber()
        def _pick_sprite_library_atlas(A):
                D='Images';E=A.sprite_lib_auto_jpg.get()
                if E:B=[(D,'*.jpg;*.jpeg;*.meta'),(_Y,_Z)]
                else:B=[(D,'*.png;*.meta'),(_Y,_Z)]
                C=filedialog.askopenfilename(title='Select atlas texture',filetypes=B)
                if C:A.sprite_lib_atlas.set(C)
        def _load_sprite_library_categories(A):
                C=A.sprite_lib_source_path.get().strip()
                if not C or not os.path.isfile(C):messagebox.showerror(_g,_h);return
                try:B=A._extract_sprite_library_categories(C)
                except Exception as D:messagebox.showerror(_c,str(D));return
                A.sprite_lib_category_combo[_AJ]=B
                if B and A.sprite_lib_category.get().strip()not in B:A.sprite_lib_category.set(B[0])
                if not B:A.sprite_lib_status.set(_y)
        def _load_sprite_library_categories_overwrite(A):
                C=A.sprite_overwrite_path.get().strip()
                if not C or not os.path.isfile(C):messagebox.showerror(_g,_h);return
                try:B=A._extract_sprite_library_categories(C)
                except Exception as D:messagebox.showerror(_c,str(D));return
                A.sprite_overwrite_category_combo[_AJ]=B
                if B and A.sprite_overwrite_category.get().strip()not in B:A.sprite_overwrite_category.set(B[0])
                if not B:A.sprite_overwrite_status.set(_y)
        def _load_sprite_library_categories_renumber(A):
                C=A.sprite_renumber_path.get().strip()
                if not C or not os.path.isfile(C):messagebox.showerror(_g,_h);return
                try:B=A._extract_sprite_library_categories(C)
                except Exception as D:messagebox.showerror(_c,str(D));return
                A.sprite_renumber_category_combo[_AJ]=B
                if B and A.sprite_renumber_category.get().strip()not in B:A.sprite_renumber_category.set(B[0])
                if not B:A.sprite_renumber_status.set(_y)
        def _start_remove_non_skin_labels(A):
                B=A.sprite_renumber_path.get().strip()
                if not B or not os.path.isfile(B):messagebox.showerror(_g,_h);return
                def C():return A._remove_non_skin_labels(B)
                def D(removed):
                        B=removed
                        if B:A.sprite_renumber_status.set(f"DONE - removed {B} non-skin label(s).")
                        else:A.sprite_renumber_status.set('No non-skin labels found in the Sprite Library.')
                A._run(lambda:A.sprite_renumber_status.set('Removing non-skin labels...'),C,D)
        def _remove_non_skin_labels(M,sprite_lib_path):
                J=sprite_lib_path
                with open(J,_G,encoding=_F,errors=_H,newline='')as H:B=H.readlines()
                K=_B;D=_B;E=_B;I=0;A=0
                while A<len(B):
                        F=B[A];L=F.strip()
                        if L==_Q:K=_C;D=_B;E=_B;A+=1;continue
                        if not K:A+=1;continue
                        if F.startswith(_M):D=_C;E=_B;A+=1;continue
                        if D and L==_T:E=_C;A+=1;continue
                        if D and E and F.startswith(_R):
                                N=F.split(_E,1)[1].strip();O=M._normalize_entry_name(N).lower()
                                if'skin'in O:A+=1;continue
                                G=A+1
                                while G<len(B):
                                        C=B[G];P=len(C)-len(C.lstrip());Q=C.strip()
                                        if C.startswith(_R):break
                                        if C.startswith(_M):break
                                        if Q and P<4:break
                                        G+=1
                                del B[A:G];I+=1;continue
                        A+=1
                if I:
                        with open(J,_D,encoding=_F,newline='')as H:H.writelines(B)
                return I
        def _start_replace_sprite_library(A):
                J='target_found';I='source_found';D=A.sprite_lib_source_path.get().strip();F=A.sprite_lib_target_path.get().strip();E=A.sprite_lib_category.get().strip();B=A.sprite_lib_atlas.get().strip();G=A.sprite_lib_auto_find.get();H=A.sprite_lib_auto_folder.get().strip();L=A.sprite_lib_auto_jpg.get()
                if not D or not os.path.isfile(D):messagebox.showerror('Missing source Sprite Library','Please select a valid source .spriteLib file.');return
                if not F or not os.path.isfile(F):messagebox.showerror('Missing target Sprite Library','Please select a valid target .spriteLib file.');return
                if G:
                        if not H or not os.path.isdir(H):messagebox.showerror('Missing atlas folder','Please select a valid atlas folder.');return
                else:
                        if not E:messagebox.showerror(_AU,_AV);return
                        if not B or not os.path.isfile(B):messagebox.showerror(_AS,'Please select a valid atlas image file.');return
                        C=B
                        if B.lower().endswith(_I):C=B[:-5]
                        N=os.path.splitext(C)[1].lower()
                        if L:K=_t,_u;M='JPG'
                        else:K=_U,;M='PNG'
                        if N not in K:messagebox.showerror('Atlas type mismatch',f"Expected a {M} atlas (toggle the JPG option if needed).");return
                O='This will modify the target Sprite Library asset in-place.\n\nContinue?'
                if G:O='This will modify the target Sprite Library asset in-place\nfor all categories using the selected folder.\n\nContinue?'
                def P():
                        O='single'
                        if G:P=A._find_assets_root(D);Q=A._build_guid_to_meta_index(P);R={};S=_C;return A._replace_sprite_library_auto_folder(D,F,H,L,R,Q,S)
                        C=B
                        if not B.lower().endswith(_I):C=B+_I
                        T=C[:-5]if C.lower().endswith(_I)else C
                        if not os.path.isfile(C):raise FileNotFoundError(f"Atlas .meta not found:\n{C}")
                        U=A._normalize_entry_name(E);V=A._extract_sprite_library_categories(D);W=any(A._normalize_entry_name(B)==U for B in V)
                        if not W:return{_z:O,_i:0,_j:0,_k:0,_l:0,I:_B,J:_B}
                        M=A._build_atlas_series(T)
                        if M:K=A._build_sprite_sequence_from_series(M)
                        else:
                                N,X=A._load_sprite_sheet_entries(C)
                                if not N:raise ValueError('Atlas .meta missing guid.')
                                K=[(A,N)for(B,A)in X]
                        if not K:raise ValueError('Atlas .meta has no sprite slice names.')
                        Y,Z,a,b=A._replace_sprite_library_category_sequential(F,E,K);return{_z:O,_i:Y,_j:Z,_k:a,_l:0,I:_C,J:b}
                def Q(result):
                        N='Missing sprite names';B=result
                        if B.get(_z)=='auto':
                                K=B.get(_AK,0)
                                if K==0:A.sprite_lib_status.set('No categories found in the source Sprite Library.');return
                                H=B.get(_i,0);D=B.get(_j,0);F=B.get(_k,0);G=B.get(_l,0);L=B.get(_AL,0);M=B.get(_AM,0);O=B.get(_a,0);C=f"DONE - updated {H} label(s) across {O}/{K} category(s)."
                                if L:C+=f" {L} category(s) missing atlas."
                                if M:C+=f" {M} category(s) missing in target."
                                if D:C+=f" {D} sprite name(s) not found in atlas."
                                if F:C+=f" {F} label(s) missing in target."
                                if G:C+=f" {G} label(s) missing sprite in source."
                                A.sprite_lib_status.set(C)
                                if D:messagebox.showwarning(N,f"{D} sprite name(s) were not found in the atlas.")
                                return
                        H=B.get(_i,0);D=B.get(_j,0);F=B.get(_k,0);G=B.get(_l,0);P=B.get(I,_C);Q=B.get(J,_C)
                        if not P:A.sprite_lib_status.set(f"Source category not found: {E}");return
                        if not Q:A.sprite_lib_status.set(f"Target category not found: {E}");return
                        C=f"DONE - updated {H} label(s)."
                        if D:C+=f" {D} sprite name(s) not found in atlas."
                        if F:C+=f" {F} label(s) missing in target."
                        if G:C+=f" {G} label(s) missing sprite in source."
                        A.sprite_lib_status.set(C)
                        if D:messagebox.showwarning(N,f"{D} sprite name(s) were not found in the atlas.")
                A._run(lambda:A.sprite_lib_status.set(_P),P,Q)
        def _start_overwrite_sprite_library(C):
                O='empty_categories';N='duplicate_matches';M='missing_category_names';L='created_categories';A=C.sprite_overwrite_path.get().strip();H=C.sprite_overwrite_category.get().strip();B=C.sprite_overwrite_root.get().strip();E=C.sprite_overwrite_file.get().strip();F=C.sprite_overwrite_target.get().strip();G=C.sprite_overwrite_auto.get()
                if not A or not os.path.isfile(A):messagebox.showerror(_g,_h);return
                if not B or not os.path.isdir(B):messagebox.showerror(_AR,'Please select a valid scan root folder.');return
                if not E:messagebox.showerror('Missing file name','Please enter a texture file name.');return
                if not F:messagebox.showerror('Missing folder name','Please enter a target folder name.');return
                if not G and not H:H=os.path.basename(os.path.normpath(B))
                if G:
                        try:D=C._extract_sprite_library_categories(A)
                        except Exception as P:messagebox.showerror(_c,str(P));return
                        if not D:messagebox.showerror(_AW,_y);return
                        I,J,K=C._find_sprite_library_category_folders(B,D)
                        if not I:messagebox.showerror('No matches','No folders matched any Sprite Library category.');return
                else:I=_A;J=[];K=0
                def Q():
                        if G:
                                P=0;Q=0;R=0;S=0;T=0;U=0;V=0
                                for(X,Y)in I:
                                        D=C._overwrite_sprite_library_from_folders(A,X,Y,F,E);U+=1;W=D.get(_m,0)
                                        if W==0:V+=1
                                        P+=W;Q+=D.get(_n,0);R+=D.get(_o,0);S+=D.get(_p,0)
                                        if D.get(_A9):T+=1
                                return{_AA:_C,_m:P,_n:Q,_o:R,_p:S,L:T,_a:U,_AB:len(J),M:J,N:K,O:V}
                        return C._overwrite_sprite_library_from_folders(A,H,B,F,E)
                def R(result):
                        S='Overwrite completed with warnings';R='No sprites found to add.';A=result
                        if A.get(_AA):
                                G=A.get(_m,0);D=A.get(_n,0);E=A.get(_o,0);F=A.get(_p,0);K=A.get(L,0);T=A.get(_a,0);I=A.get(_AB,0);P=A.get(M,[]);J=A.get(N,0);Q=A.get(O,0)
                                if G==0:C.sprite_overwrite_status.set(R);return
                                B=f"DONE - added {G} sprite(s) across {T} category(ies)."
                                if K:B+=f" Created {K} category(ies)."
                                if Q:B+=f" {Q} category(ies) had no sprites."
                                if I:B+=f" {I} category folder(s) missing."
                                if P:B+=' Missing folders: '+', '.join(P)+'.'
                                if J:B+=f" {J} extra folder match(es) ignored."
                                if D:B+=f" {D} missing file(s)."
                                if E:B+=f" {E} file(s) missing .meta."
                                if F:B+=f" {F} file(s) had no sprites."
                                C.sprite_overwrite_status.set(B)
                                if D or E or F or I or J:messagebox.showwarning(S,B)
                                return
                        G=A.get(_m,0);D=A.get(_n,0);E=A.get(_o,0);F=A.get(_p,0);U=A.get(_A9,_B);V=A.get(_AN,H)
                        if G==0:C.sprite_overwrite_status.set(R);return
                        B=f"DONE - added {G} sprite(s) to '{V}'."
                        if U:B+=' Created category.'
                        if D:B+=f" {D} missing file(s)."
                        if E:B+=f" {E} file(s) missing .meta."
                        if F:B+=f" {F} file(s) had no sprites."
                        C.sprite_overwrite_status.set(B)
                        if D or E or F:messagebox.showwarning(S,B)
                C._run(lambda:C.sprite_overwrite_status.set(_P),Q,R)
        def _start_renumber_sprite_library(A):
                H='total_updated'
                B=A.sprite_renumber_path.get().strip()
                C=A.sprite_renumber_category.get().strip()
                E=A.sprite_renumber_prefix.get()
                F=A.sprite_renumber_suffix.get()
                D=A.sprite_renumber_auto.get()
                G=A.sprite_renumber_alpha.get()
                if not B or not os.path.isfile(B):messagebox.showerror(_g,_h);return
                if not D and not C:messagebox.showerror(_AU,_AV);return
                if D:
                        try:I=A._extract_sprite_library_categories(B)
                        except Exception as J:messagebox.showerror(_c,str(J));return
                        if not I:messagebox.showerror(_AW,_y);return
                def J():
                        if D:
                                K=0;L=0;M=0
                                for N in I:
                                        O,P=A._renumber_sprite_library_category(B,N,E,F,G);L+=1
                                        if not P:M+=1
                                        K+=O
                                return{_AA:_C,H:K,_a:L,_AB:M}
                        return A._renumber_sprite_library_category(B,C,E,F,G)
                def K(result):
                        B=result
                        if isinstance(B,dict)and B.get(_AA):
                                F=B.get(H,0);G=B.get(_a,0);D=B.get(_AB,0);E=f"DONE - resorted {F} label(s) across {G} category(ies)."
                                if D:E+=f" {D} category(ies) not found."
                                A.sprite_renumber_status.set(E);return
                        I,J=B
                        if not J:A.sprite_renumber_status.set(f"Category not found: {C}");return
                        A.sprite_renumber_status.set(f"DONE - resorted {I} label(s).")
                A._run(lambda:A.sprite_renumber_status.set(_P),J,K)
        def _overwrite_sprite_library_from_folders(A,sprite_lib_path,category_name,root_folder,target_subfolder,file_name):
                I=root_folder;C=category_name;D=[]
                for(E,J,J)in os.walk(I):
                        if E==I:continue
                        if os.path.basename(E)==target_subfolder:D.append(E)
                D.sort();B=[];F=0;G=0;H=0
                for K in D:
                        Q=A._build_label_base_from_folder(K);L,M=A._resolve_overwrite_groups(K,file_name,Q)
                        if M:F+=M
                        if not L:continue
                        for(R,S)in L:
                                N=1
                                for T in S:
                                        O,P=A._load_sprite_entries_from_meta(T)
                                        if not O:G+=1;continue
                                        if not P:H+=1;continue
                                        for(J,U)in P:V=f"{R}_{N}";B.append({'label':V,'file_id':U,_N:O});N+=1
                if not B:return{_m:0,_n:F,_o:G,_p:H,_A9:_B,_AN:C}
                W=A._overwrite_sprite_library_category(sprite_lib_path,C,B);return{_m:len(B),_n:F,_o:G,_p:H,_A9:W,_AN:C}
        def _build_label_base_from_folder(C,folder):
                A=os.path.dirname(folder);B=os.path.dirname(A)
                if not A or not B:return
                return f"{os.path.basename(B)}_{os.path.basename(A)}"
        def _resolve_overwrite_groups(E,folder,file_name,base_default):
                H=base_default;G=file_name;F=folder;I,B=E._resolve_overwrite_files(F,G)
                if I:
                        if not H:return[],B
                        return[(H,I)],B
                A=[];C=0;D=[]
                for J in os.scandir(F):
                        if J.is_dir():D.append(J.path)
                D.sort()
                for K in D:
                        L,M=E._resolve_overwrite_files(K,G)
                        if L:A.append((os.path.basename(K),L))
                        elif M:C+=1
                if A:return A,C
                return A,B+C
        def _resolve_overwrite_files(I,folder,file_name):
                E=folder;A=file_name;J='.'not in A and A and not A[0].isdigit()and len(A)<=4
                if J:
                        B=[]
                        for D in os.scandir(E):
                                if not D.is_file():continue
                                if D.name.lower().endswith(_I):continue
                                if A.lower()in D.name.lower():B.append(D.path)
                        B.sort(key=I._sort_file_by_numeric_name);return B,_B
                F,G=os.path.splitext(A)
                if G and F.isdigit():
                        B=[];H=int(F)
                        while _C:
                                C=os.path.join(E,f"{H}{G}")
                                if not os.path.isfile(C):break
                                B.append(C);H+=1
                        if not B:return[],_C
                        return B,_B
                C=os.path.join(E,A)
                if not os.path.isfile(C):return[],_C
                return[C],_B
        def _find_sprite_library_category_folders(E,root_folder,categories):
                C={};D={}
                for F in categories:
                        A=E._normalize_entry_name(F).lower()
                        if A not in C:C[A]=F;D[A]=[]
                for(G,L,M)in os.walk(root_folder):
                        H=os.path.basename(G)
                        if not H:continue
                        A=E._normalize_entry_name(H).lower()
                        if A in D:D[A].append(G)
                I=[];J=[];K=0
                for(A,B)in D.items():
                        if not B:J.append(C[A]);continue
                        B.sort(key=lambda p:(len(Path(p).parts),p.lower()))
                        if len(B)>1:K+=len(B)-1
                        I.append((C[A],B[0]))
                return I,J,K
        def _sort_file_by_numeric_name(B,path):
                A=Path(path).stem
                if A.isdigit():return int(A)
                return 2**31-1
        def _load_sprite_entries_from_meta(D,image_path):
                B=image_path+_I
                if not os.path.isfile(B):return _A,[]
                A,C=D._load_sprite_sheet_entries(B)
                if not A:return _A,[]
                if not C:return A,[]
                return A,C
        def _sort_sprite_names_by_frame_index(F,names):
                A={};D=[]
                for B in names:
                        C=B.split('_')
                        if len(C)>=2 and C[1].isdigit():A[int(C[1])]=B
                        else:D.append(B)
                E=D;E.extend([A[B]for B in sorted(A)]);return E
        def _overwrite_sprite_library_category(B,sprite_lib_path,category_name,entries):
                F=category_name;E=sprite_lib_path
                with open(E,_G,encoding=_F,errors=_H,newline='')as D:A=D.readlines()
                if not A:raise ValueError('Sprite Library file is empty.')
                L=B._detect_line_ending(A);M,G,N=B._index_sprite_library_categories_exact(A)
                if M is _A:raise ValueError('Sprite Library file is missing m_Library.')
                O=B._normalize_entry_name(F);C=_A
                for H in N:
                        if H[_AC]==O:C=H;break
                I=B._build_sprite_library_category_block(F,entries,L)
                if C:A=A[:C[_A0]]+I+A[C[_V]:];J=_B
                else:K=G if G is not _A else len(A);A=A[:K]+I+A[K:];J=_C
                with open(E,_D,encoding=_F,newline='')as D:D.writelines(A)
                return J
        def _build_sprite_library_category_block(C,category_name,entries,line_ending):
                E=entries;D=category_name;A=line_ending;F=C._format_sprite_label(D,D);G=C._sprite_library_hash(D);B=[f"  - m_Name: {F}{A}",f"    m_Hash: {G}{A}",f"    m_CategoryList: []{A}"]
                if E:B.append(f"    m_OverrideEntries:{A}");B.extend(C._build_sprite_library_override_entries(E,A))
                else:B.append(f"    m_OverrideEntries: []{A}")
                B.append(f"    m_FromMain: 0{A}");B.append(f"    m_EntryOverrideCount: {len(E)}{A}");return B
        def _build_sprite_library_override_entries(E,entries,line_ending):
                B=line_ending;A=[]
                for C in entries:D=C['label'];H=E._format_sprite_label(D,D);I=E._sprite_library_hash(D);F=C['file_id'];G=C[_N];A.append(f"    - m_Name: {H}{B}");A.append(f"      m_Hash: {I}{B}");A.append(f"      m_Sprite: {{fileID: {F}, guid: {G}, type: 3}}{B}");A.append(f"      m_FromMain: 0{B}");A.append(f"      m_SpriteOverride: {{fileID: {F}, guid: {G}, type: 3}}{B}")
                return A
        def _sprite_library_hash(B,value):
                A=value
                if not A:return 0
                return zlib.crc32(A.encode(_F))&1073741823
        def _detect_line_ending(B,lines):
                for A in lines:
                        if A.endswith(_AD):return _AD
                        if A.endswith(_q):return _q
                return _q
        def _index_sprite_library_categories_exact(L,lines):
                G=lines;E=[];H=_A;B=_A;F=_B;A=_A
                for(D,C)in enumerate(G):
                        I=C.strip()
                        if I==_Q:F=_C;H=D;continue
                        if not F:continue
                        J=len(C)-len(C.lstrip())
                        if J==2 and C.startswith(_M):
                                if A:A[_V]=D;E.append(A)
                                K=C.split(_E,1)[1].strip();A={'name':K,_AC:L._normalize_entry_name(K),_A0:D};continue
                        if J==2 and I!=''and not C.startswith('  - '):
                                B=D
                                if A:A[_V]=D;E.append(A);A=_A
                                F=_B;break
                if F and A:A[_V]=B if B is not _A else len(G);E.append(A)
                if H is not _A and B is _A:B=len(G)
                return H,B,E
        def _extract_sprite_library_categories(H,sprite_lib_path):
                A=[]
                with open(sprite_lib_path,_G,encoding=_F,errors=_H,newline='')as E:F=E.readlines()
                D=_B
                for B in F:
                        G=B.strip()
                        if G==_Q:D=_C;continue
                        if D and B.startswith(_M):
                                C=B.split(_E,1)[1].strip()
                                if C and C not in A:A.append(C)
                return A
        def _index_sprite_library_categories(I,lines):
                E=lines;C=[];D=_B;A=_A
                for(F,B)in enumerate(E):
                        G=B.strip()
                        if G==_Q:D=_C;continue
                        if D and B.startswith(_M):
                                if A:A[_V]=F;C.append(A)
                                H=B.split(_E,1)[1].strip();A={'name':H,_AC:I._normalize_entry_name(H),_A0:F};continue
                        if D and G!=''and not B.startswith('  '):break
                if A:A[_V]=len(E);C.append(A)
                return C
        def _extract_sprite_library_source_map(C,sprite_lib_path,guid_to_fileid_name=_A,guid_to_meta_path=_A,guid_index_complete=_B):
                T=guid_index_complete;S=sprite_lib_path;M=guid_to_meta_path;L=guid_to_fileid_name
                with open(S,_G,encoding=_F,errors=_H,newline='')as X:Y=X.readlines()
                if L is _A:L={}
                if M is _A:M={}
                U=[];D={};V={};Q=_B;H=_B;F=_A;B=_A;G=_B;W=C._find_assets_root(S)
                for E in Y:
                        N=E.strip()
                        if N==_Q:Q=_C;H=_B;F=_A;B=_A;G=_B;continue
                        if Q and E.startswith(_M):
                                R=E.split(_E,1)[1].strip();I=C._normalize_entry_name(R);U.append(R);A=D.get(I)
                                if not A:A={'name':R,_b:{},_A1:{}};D[I]=A
                                F=I;H=_B;B=_A;G=_B;continue
                        if not Q or not F:continue
                        if N==_T:H=_C;B=_A;G=_B;continue
                        if H and E.startswith(_R):
                                B=C._normalize_entry_name(E.split(_E,1)[1].strip());A=D[F]
                                if B not in A[_b]:A[_b][B]=_A;A[_A1][B]=_A
                                G=_B;continue
                        if H and B:
                                if N.startswith(_W):
                                        O,P=C._parse_sprite_ref_line(E);Z=C._has_valid_sprite_ref(O,P);J,K=C._sprite_name_and_meta_from_ref(O,P,W,L,M,T);A=D[F]
                                        if K:A[_A1][B]=K
                                        if J:A[_b][B]=J
                                        if Z:G=_C
                                        continue
                                if N.startswith(_X):
                                        if G:continue
                                        O,P=C._parse_sprite_ref_line(E);J,K=C._sprite_name_and_meta_from_ref(O,P,W,L,M,T);A=D[F]
                                        if K:A[_A1][B]=K
                                        if J:A[_b][B]=J
                for(I,A)in D.items():V[I]=sum(1 for A in A[_b].values()if not A)
                return U,D,V
        def _extract_sprite_library_label_sprite_names(B,sprite_lib_path,category_name,guid_to_fileid_name=_A,guid_to_meta_path=_A,guid_index_complete=_B):
                Q=guid_index_complete;P=sprite_lib_path;K=guid_to_meta_path;J=guid_to_fileid_name
                with open(P,_G,encoding=_F,errors=_H,newline='')as U:V=U.readlines()
                D={};L={};R=_B;S=_B;E=_B;G=_B;A=_A;F=_B;T=B._find_assets_root(P)
                if J is _A:J={}
                if K is _A:K={}
                W=B._normalize_entry_name(category_name)
                for C in V:
                        M=C.strip()
                        if M==_Q:S=_C;E=_B;G=_B;A=_A;F=_B;continue
                        if S and C.startswith(_M):
                                X=B._normalize_entry_name(C.split(_E,1)[1].strip());E=X==W
                                if E:R=_C
                                G=_B;A=_A;F=_B;continue
                        if E and M==_T:G=_C;A=_A;F=_B;continue
                        if E and G and C.startswith(_R):
                                A=B._normalize_entry_name(C.split(_E,1)[1].strip())
                                if A not in D:D[A]=_A;L[A]=_A
                                F=_B;continue
                        if E and G and A:
                                if M.startswith(_W):
                                        N,O=B._parse_sprite_ref_line(C);Y=B._has_valid_sprite_ref(N,O);H,I=B._sprite_name_and_meta_from_ref(N,O,T,J,K,Q)
                                        if I:L[A]=I
                                        if H:D[A]=H
                                        if Y:F=_C
                                        continue
                                if M.startswith(_X):
                                        if F:continue
                                        N,O=B._parse_sprite_ref_line(C);H,I=B._sprite_name_and_meta_from_ref(N,O,T,J,K,Q)
                                        if I:L[A]=I
                                        if H:D[A]=H
                Z=sum(1 for A in D.values()if not A);return D,L,R,Z
        def _has_valid_sprite_ref(C,file_id,guid):
                B=guid;A=file_id
                if not A or not B:return _B
                A=A.strip();B=B.strip()
                if A=='0':return _B
                if B==_AX:return _B
                return _C
        def _sprite_name_and_meta_from_ref(G,file_id,guid,assets_root,guid_to_fileid_name,guid_to_meta_path,guid_index_complete=_B):
                F=guid_to_meta_path;D=guid_to_fileid_name;C=file_id;A=guid
                if not C or not A:return _A,_A
                C=C.strip();A=A.strip()
                if C=='0':return _A,_A
                if A==_AX:return _A,_A
                B=F.get(A)
                if B is _A:
                        if guid_index_complete:F[A]=_A;D[A]={};return _A,_A
                        B=G._find_meta_by_guid(assets_root,A);F[A]=B
                if not B:D[A]={};return _A,_A
                E=D.get(A)
                if E is _A:E=G._build_fileid_to_name(B);D[A]=E
                return E.get(C),B
        def _sprite_name_from_ref(A,file_id,guid,assets_root,guid_to_fileid_name):B,C=A._sprite_name_and_meta_from_ref(file_id,guid,assets_root,guid_to_fileid_name,{});return B
        def _build_atlas_meta_index(A,root_folder,use_jpg):
                G=(_t,_u)if use_jpg else(_U,);B={};C={};H=list(walk(root_folder,G))
                def I(image_path):
                        B=image_path;C=B+_I
                        if not os.path.isfile(C):return
                        D,E=A._parse_sprite_meta(C)
                        if not D or not E:return
                        return B,D,E
                J=A._parallel_map(H,I,A._io_workers)
                for D in J:
                        if not D:continue
                        E,K,F=D;B[E]={_N:K,_S:F}
                        for L in F:C.setdefault(L,set()).add(E)
                return B,C
        def _find_best_atlas_for_sprites(D,sprite_names,name_index):
                A={}
                for C in sprite_names:
                        for B in name_index.get(C,()):A[B]=A.get(B,0)+1
                if not A:return
                return max(A.items(),key=lambda item:(item[1],item[0]))[0]
        def _build_atlas_series_cached(L,atlas_file,meta_cache):
                C=Path(atlas_file);M=C.stem;F=re.match(_AO,M)
                if not F:return
                G,H=F.groups();A=int(H);I=len(H);N=C.suffix;O=C.parent;J=[]
                while _C:
                        if I>1:B=f"{G}{A:0{I}d}"
                        else:B=f"{G}{A}"
                        D=os.path.normpath(str(O/f"{B}{N}"));E=meta_cache.get(D)
                        if not E:break
                        K=E[_S];P=E[_N];Q=L._count_numeric_suffixes(K,B);J.append({'num':A,_AE:B,'path':D,'meta':D+_I,_N:P,_S:K,_AP:Q});A+=1
                return J or _A
        def _normalize_atlas_series_start(P,atlas_path,meta_cache):
                D=atlas_path;E=Path(D);N=E.stem;G=re.match(_AO,N)
                if not G:return os.path.normpath(D)
                B,H=G.groups();I=int(H);C=len(H);J=E.suffix;K=E.parent;A=I
                while A>0:
                        F=A-1
                        if C>1:L=f"{B}{F:0{C}d}"
                        else:L=f"{B}{F}"
                        O=os.path.normpath(str(K/f"{L}{J}"))
                        if O not in meta_cache:break
                        A=F
                if A==I:return os.path.normpath(D)
                if C>1:M=f"{B}{A:0{C}d}"
                else:M=f"{B}{A}"
                return os.path.normpath(str(K/f"{M}{J}"))
        def _get_cached_atlas_data(G,atlas_path,meta_cache,series_cache):
                H=series_cache;D=meta_cache;A=atlas_path;A=G._normalize_atlas_series_start(A,D);I=H.get(A)
                if I:return I
                B=G._build_atlas_series_cached(A,D)
                if B:E=B[0][_N];F=B[0][_S]
                else:C=D.get(A);E=C[_N]if C else _A;F=C[_S]if C else _A
                H[A]=E,F,B;return E,F,B
        def _path_is_under(C,path,root):
                if not path or not root:return _B
                A=os.path.normcase(os.path.normpath(path));B=os.path.normcase(os.path.normpath(root))
                if A==B:return _C
                return A.startswith(B+os.sep)
        def _label_paths_under_root(D,label_to_meta,root_folder):
                B=label_to_meta
                if not B:return _B
                C=_B
                for A in B.values():
                        if not A:continue
                        C=_C;E=A[:-5]if A.lower().endswith(_I)else A
                        if not D._path_is_under(E,root_folder):return _B
                return C
        def _map_source_to_target_image(G,source_meta_path,root_folder,use_jpg,path_cache):
                D=root_folder;C=path_cache;B=source_meta_path
                if not B:return
                A=B[:-5]if B.lower().endswith(_I)else B;F=C.get(A)
                if F is not _A:return F
                if not G._path_is_under(A,D):C[A]=_A;return
                H=os.path.relpath(A,D);I=os.path.splitext(H)[0];J=(_t,_u)if use_jpg else(_U,)
                for K in J:
                        E=os.path.join(D,I+K)
                        if os.path.isfile(E):C[A]=E;return E
                C[A]=_A
        def _resolve_target_sprite_name(E,source_name,name_to_id):
                B=name_to_id;A=source_name
                if not B:return
                if not A:return
                if A and A in B:return A
                if A:
                        C=re.sub('^(\\d+)_','\\1N_',A)
                        if C!=A and C in B:return C
                        D=re.sub('^(\\d+)N_','\\1_',A)
                        if D!=A and D in B:return D
                if len(B)==1:return next(iter(B))
        def _get_target_meta_data(F,image_path,meta_cache):
                B=meta_cache;A=image_path
                if A in B:return B[A]
                C=A+_I
                if not os.path.isfile(C):B[A]=_A,_A;return _A,_A
                D,E=F._parse_sprite_meta(C);B[A]=D,E;return D,E
        def _replace_sprite_library_auto_folder(A,source_path,target_path,root_folder,use_jpg,guid_to_fileid_name=_A,guid_to_meta_path=_A,guid_index_complete=_B):
                V=use_jpg;U=target_path;H=guid_to_meta_path;G=guid_to_fileid_name;F=root_folder
                if G is _A:G={}
                if H is _A:H={}
                I,e,f=A._extract_sprite_library_source_map(source_path,G,H,guid_index_complete)
                if not I:return{_z:'auto',_i:0,_j:0,_k:0,_l:0,_AL:0,_AM:0,_AK:0,_a:0}
                with open(U,_G,encoding=_F,errors=_H,newline='')as J:C=J.readlines()
                g=A._index_sprite_library_categories(C);h={A[_AC]:A for A in g};D=_A;W=_A;i={};E=0;K=0;L=0;X=0;M=0;Y=0;N=0
                for j in I:
                        O=A._normalize_entry_name(j);P=e.get(O)
                        if not P:continue
                        Q=P[_b];Z=P[_A1];X+=f.get(O,0);B=h.get(O)
                        if not B:Y+=1;continue
                        if A._label_paths_under_root(Z,F):R,S,T=A._replace_sprite_library_category_from_source_by_path_lines(C,B[_A0],B[_V],Q,Z,F,V);N+=1;E+=R;K+=S;L+=T;continue
                        a={A for A in Q.values()if A}
                        if not a:continue
                        if D is _A:
                                D,W=A._build_atlas_meta_index(F,V)
                                if not D:raise ValueError('No atlas .meta files found in the selected folder.')
                        b=A._find_best_atlas_for_sprites(a,W)
                        if not b:M+=1;continue
                        c,d,k=A._get_cached_atlas_data(b,D,i)
                        if not c or not d:M+=1;continue
                        R,S,T=A._replace_sprite_library_category_from_source_lines(C,B[_A0],B[_V],Q,c,d,k);N+=1;E+=R;K+=S;L+=T
                if E>0:
                        with open(U,_D,encoding=_F,newline='')as J:J.writelines(C)
                return{_z:'auto',_i:E,_j:K,_k:L,_l:X,_AL:M,_AM:Y,_AK:len(I),_a:N}
        def _find_atlas_sprite_id(E,sprite_name,atlas_guid,name_to_id,atlas_series):
                C=atlas_series;B=sprite_name
                if C:
                        for D in C:
                                A=D[_S].get(B)
                                if A:return A,D[_N]
                A=name_to_id.get(B)
                if A:return A,atlas_guid
                return _A,_A
        def _replace_sprite_library_category_from_source_by_path_lines(C,lines,start,end,label_to_sprite,label_to_meta,root_folder,use_jpg):
                N=label_to_sprite;M=lines;O=0;P=0;G=_B;B=_A;D=_A;H=_A;E=_B;Q=set();V={};W={};A=start
                while A<end:
                        I=M[A];J=I.strip()
                        if J==_T:G=_C;B=_A;E=_B;A+=1;continue
                        if G and I.startswith(_R):
                                B=C._normalize_entry_name(I.split(_E,1)[1].strip());Q.add(B);D=_A;H=_A;E=_B;K=N.get(B);R=label_to_meta.get(B)
                                if K and R:
                                        S=C._map_source_to_target_image(R,root_folder,use_jpg,W)
                                        if S:
                                                T,L=C._get_target_meta_data(S,V)
                                                if T and L:
                                                        U=C._resolve_target_sprite_name(K,L)
                                                        if U:D=L.get(U);H=T
                                if K and D is _A:P+=1
                                A+=1;continue
                        if G and B:
                                F=_A
                                if J.startswith(_X):F=_r
                                elif J.startswith(_W):F=_s
                                if F:
                                        if D is _A:A+=1;continue
                                        A=C._update_sprite_ref_line(M,A,F,D,H)
                                        if not E:O+=1;E=_C
                                        continue
                        A+=1
                X=len([A for(A,B)in N.items()if B and A not in Q]);return O,P,X
        def _replace_sprite_library_category_from_source_lines(F,lines,start,end,label_to_sprite,atlas_guid,name_to_id,atlas_series):
                M=label_to_sprite;L=lines;G=atlas_guid;N=0;O=0;H=_B;B=_A;C=_A;I=G;D=_B;P=set();A=start
                while A<end:
                        J=L[A];K=J.strip()
                        if K==_T:H=_C;B=_A;D=_B;A+=1;continue
                        if H and J.startswith(_R):
                                B=F._normalize_entry_name(J.split(_E,1)[1].strip());P.add(B);C=_A;I=G;D=_B;Q=M.get(B)
                                if Q:
                                        C,I=F._find_atlas_sprite_id(Q,G,name_to_id,atlas_series)
                                        if C is _A:O+=1
                                A+=1;continue
                        if H and B:
                                E=_A
                                if K.startswith(_X):E=_r
                                elif K.startswith(_W):E=_s
                                if E:
                                        if C is _A:A+=1;continue
                                        A=F._update_sprite_ref_line(L,A,E,C,I)
                                        if not D:N+=1;D=_C
                                        continue
                        A+=1
                R=len([A for(A,B)in M.items()if B and A not in P]);return N,O,R
        def _replace_sprite_library_category_from_source_by_path(C,sprite_lib_path,category_name,label_to_sprite,label_to_meta,root_folder,use_jpg):
                S=label_to_sprite;R=sprite_lib_path
                with open(R,_G,encoding=_F,errors=_H,newline='')as L:I=L.readlines()
                M=0;T=0;N=_B;U=_B;D=_B;F=_B;B=_A;G=_A;O=_A;E=_B;V=set();a=C._normalize_entry_name(category_name);b={};c={};A=0
                while A<len(I):
                        H=I[A];J=H.strip()
                        if J==_Q:U=_C;D=_B;F=_B;B=_A;E=_B;A+=1;continue
                        if U and H.startswith(_M):
                                d=C._normalize_entry_name(H.split(_E,1)[1].strip());D=d==a
                                if D:N=_C
                                F=_B;B=_A;E=_B;A+=1;continue
                        if D and J==_T:F=_C;B=_A;E=_B;A+=1;continue
                        if D and F and H.startswith(_R):
                                B=C._normalize_entry_name(H.split(_E,1)[1].strip());V.add(B);G=_A;O=_A;E=_B;P=S.get(B);W=label_to_meta.get(B)
                                if P and W:
                                        X=C._map_source_to_target_image(W,root_folder,use_jpg,c)
                                        if X:
                                                Y,Q=C._get_target_meta_data(X,b)
                                                if Y and Q:
                                                        Z=C._resolve_target_sprite_name(P,Q)
                                                        if Z:G=Q.get(Z);O=Y
                                if P and G is _A:T+=1
                                A+=1;continue
                        if D and F and B:
                                K=_A
                                if J.startswith(_X):K=_r
                                elif J.startswith(_W):K=_s
                                if K:
                                        if G is _A:A+=1;continue
                                        A=C._update_sprite_ref_line(I,A,K,G,O)
                                        if not E:M+=1;E=_C
                                        continue
                        A+=1
                e=len([A for(A,B)in S.items()if B and A not in V])
                if N and M>0:
                        with open(R,_D,encoding=_F,newline='')as L:L.writelines(I)
                return M,T,e,N
        def _replace_sprite_library_category_sequential(G,sprite_lib_path,category_name,sprite_sequence):
                R=sprite_sequence;Q=sprite_lib_path
                with open(Q,_G,encoding=_F,errors=_H,newline='')as L:H=L.readlines()
                M=0;S=0;U=0;N=_B;T=_B;B=_B;D=_B;E=_A;I=_A;O=_A;C=_B;V=G._normalize_entry_name(category_name);P=0;A=0
                while A<len(H):
                        F=H[A];J=F.strip()
                        if J==_Q:T=_C;B=_B;D=_B;E=_A;C=_B;A+=1;continue
                        if T and F.startswith(_M):
                                W=G._normalize_entry_name(F.split(_E,1)[1].strip());B=W==V
                                if B:N=_C
                                D=_B;E=_A;C=_B;A+=1;continue
                        if B and J==_T:D=_C;E=_A;C=_B;A+=1;continue
                        if B and D and F.startswith(_R):
                                E=G._normalize_entry_name(F.split(_E,1)[1].strip());I=_A;O=_A;C=_B
                                if P<len(R):I,O=R[P];P+=1
                                else:S+=1
                                A+=1;continue
                        if B and D and E:
                                K=_A
                                if J.startswith(_X):K=_r
                                elif J.startswith(_W):K=_s
                                if K:
                                        if I is _A:A+=1;continue
                                        A=G._update_sprite_ref_line(H,A,K,I,O)
                                        if not C:M+=1;C=_C
                                        continue
                        A+=1
                if N and M>0:
                        with open(Q,_D,encoding=_F,newline='')as L:L.writelines(H)
                return M,S,U,N
        def _replace_sprite_library_category_from_source(E,sprite_lib_path,category_name,label_to_sprite,atlas_guid,name_to_id,atlas_series):
                R=label_to_sprite;Q=sprite_lib_path;L=atlas_guid
                with open(Q,_G,encoding=_F,errors=_H,newline='')as M:I=M.readlines()
                N=0;S=0;O=_B;T=_B;C=_B;F=_B;B=_A;G=_A;P=L;D=_B;U=set();W=E._normalize_entry_name(category_name);A=0
                while A<len(I):
                        H=I[A];J=H.strip()
                        if J==_Q:T=_C;C=_B;F=_B;B=_A;D=_B;A+=1;continue
                        if T and H.startswith(_M):
                                X=E._normalize_entry_name(H.split(_E,1)[1].strip());C=X==W
                                if C:O=_C
                                F=_B;B=_A;D=_B;A+=1;continue
                        if C and J==_T:F=_C;B=_A;D=_B;A+=1;continue
                        if C and F and H.startswith(_R):
                                B=E._normalize_entry_name(H.split(_E,1)[1].strip());U.add(B);G=_A;P=L;D=_B;V=R.get(B)
                                if V:
                                        G,P=E._find_atlas_sprite_id(V,L,name_to_id,atlas_series)
                                        if G is _A:S+=1
                                A+=1;continue
                        if C and F and B:
                                K=_A
                                if J.startswith(_X):K=_r
                                elif J.startswith(_W):K=_s
                                if K:
                                        if G is _A:A+=1;continue
                                        A=E._update_sprite_ref_line(I,A,K,G,P)
                                        if not D:N+=1;D=_C
                                        continue
                        A+=1
                Y=len([A for(A,B)in R.items()if B and A not in U])
                if O and N>0:
                        with open(Q,_D,encoding=_F,newline='')as M:M.writelines(I)
                return N,S,Y,O
        def _parse_sprite_meta(C,meta_path):
                with open(meta_path,_G,encoding=_F,errors=_H,newline='')as I:B=I.readlines()
                D=_A
                for J in B:
                        E=J.strip()
                        if E.startswith(_A2):D=E.split(_E,1)[1].strip();break
                F=C._parse_name_file_id_table(B);G=C._parse_sprite_sheet_table(B);A={}
                if G:A.update(G)
                if F:
                        for(H,K)in F.items():
                                if H not in A:A[H]=K
                return D,A
        def _load_sprite_sheet_entries(B,meta_path):
                with open(meta_path,_G,encoding=_F,errors=_H,newline='')as F:C=F.readlines()
                D=_A
                for G in C:
                        E=G.strip()
                        if E.startswith(_A2):D=E.split(_E,1)[1].strip();break
                A=B._parse_sprite_sheet_entries(C)
                if A:A=B._sort_sprite_sheet_entries(A)
                return D,A
        def _parse_sprite_sheet_entries(J,lines):
                K=[];L=_B;M=0;E=_B;I=0;A=_B;C=_A;D=_A
                def F():
                        if C is not _A and D is not _A:K.append((C,D))
                for G in lines:
                        B=G.strip()
                        if B.startswith(_AH):L=_C;M=len(G)-len(G.lstrip());E=_B;A=_B;C=_A;D=_A;continue
                        if not L:continue
                        if B=='':continue
                        H=len(G)-len(G.lstrip())
                        if H<=M:
                                if A:F()
                                break
                        if B.startswith(_A3):E=_C;I=H;A=_B;C=_A;D=_A;continue
                        if not E:continue
                        if H<I:
                                if A:F()
                                E=_B;A=_B;C=_A;D=_A;continue
                        if H==I:
                                if B.startswith('-'):
                                        if A:F()
                                        A=_C;C=_A;D=_A
                                        if B.startswith('- name:'):C=J._normalize_entry_name(B.split(_E,1)[1].strip())
                                        continue
                                if A:F()
                                E=_B;A=_B;C=_A;D=_A;continue
                        if not A:continue
                        if B.startswith(_x):C=J._normalize_entry_name(B.split(_E,1)[1].strip());continue
                        if B.startswith(_AY):D=B.split(_E,1)[1].strip();continue
                if A:F()
                return K
        def _sort_sprite_sheet_entries(H,entries):
                A=[];C=[]
                for(D,(B,E))in enumerate(entries):
                        F=H._sprite_sheet_entry_index(B)
                        if F is _A:C.append((D,B,E))
                        else:A.append((F,D,B,E))
                A.sort(key=lambda item:(item[0],item[1]));G=[(B,C)for(A,A,B,C)in A];G.extend((A,B)for(C,A,B)in C);return G
        def _sprite_sheet_entry_index(E,name):
                A=name
                if not A:return
                D=A.split('_')
                for B in reversed(D):
                        if B.isdigit():return int(B)
                C=re.search('(\\d+)$',A)
                if C:return int(C.group(1))
        def _build_sprite_sequence_from_series(D,atlas_series):
                A=[]
                for B in atlas_series:
                        E=B[_N];F,C=D._load_sprite_sheet_entries(B['meta'])
                        if not C:continue
                        A.extend((A,E)for(B,A)in C)
                return A
        def _parse_internal_id_table(I,lines):
                D={};E=_B;F=0;B=_A
                for C in lines:
                        A=C.strip()
                        if A.startswith(_AQ):E=_C;F=len(C)-len(C.lstrip());B=_A;continue
                        if not E:continue
                        if A=='':continue
                        G=len(C)-len(C.lstrip())
                        if G<=F and not A.startswith('-'):break
                        if A.startswith('213:'):B=A.split(_E,1)[1].strip();continue
                        if A.startswith('second:'):
                                H=A.split(_E,1)[1].strip()
                                if B is not _A:D[H]=B;B=_A
                return D
        def _parse_name_file_id_table(I,lines):
                C={};D=_B;E=0
                for A in lines:
                        B=A.strip()
                        if B.startswith(_AG):D=_C;E=len(A)-len(A.lstrip());continue
                        if not D:continue
                        if B=='':continue
                        F=len(A)-len(A.lstrip())
                        if F<=E:break
                        if _E in B:G,H=B.split(_E,1);C[G.strip()]=H.strip()
                return C
        def _parse_sprite_sheet_table(J,lines):
                H={};K=_B;L=0;E=_B;I=0;D=_B;A=_A;C=_A
                for F in lines:
                        B=F.strip()
                        if B.startswith(_AH):K=_C;L=len(F)-len(F.lstrip());E=_B;D=_B;A=_A;C=_A;continue
                        if not K:continue
                        if B=='':continue
                        G=len(F)-len(F.lstrip())
                        if G<=L:break
                        if B.startswith(_A3):E=_C;I=G;D=_B;A=_A;C=_A;continue
                        if not E:continue
                        if G<I:E=_B;D=_B;A=_A;C=_A;continue
                        if G==I:
                                if B.startswith('-'):
                                        D=_C;A=_A;C=_A
                                        if B.startswith('- name:'):A=J._normalize_entry_name(B.split(_E,1)[1].strip())
                                        continue
                                E=_B;D=_B;A=_A;C=_A;continue
                        if not D:continue
                        if B.startswith(_x):
                                A=J._normalize_entry_name(B.split(_E,1)[1].strip())
                                if C is not _A:H[A]=C;A=_A;C=_A
                                continue
                        if B.startswith(_AY):
                                M=B.split(_E,1)[1].strip()
                                if A:H[A]=M;A=_A
                                else:C=M
                return H
        def _index_to_alpha_label(B,index):
                A=index
                if A<=0:return''
                C=[]
                while A>0:
                        A-=1;C.append(chr(ord('A')+A%26));A//=26
                C.reverse()
                return''.join(C)
        def _renumber_sprite_library_category(D,sprite_lib_path,category_name,prefix,suffix,use_alpha=_B):
                J=sprite_lib_path
                with open(J,_G,encoding=_F,errors=_H,newline='')as E:F=E.readlines()
                G=0;H=_B;K=_B;A=_B;C=_B;I=1;M=D._normalize_entry_name(category_name)
                for(N,B)in enumerate(F):
                        L=B.strip()
                        if L==_Q:K=_C;A=_B;C=_B;continue
                        if K and B.startswith(_M):
                                O=D._normalize_entry_name(B.split(_E,1)[1].strip());A=O==M
                                if A:H=_C;I=1
                                C=_B;continue
                        if A and L==_T:C=_C;continue
                        if A and C and B.startswith(_R):
                                P=D._index_to_alpha_label(I)if use_alpha else str(I);Q=f"{prefix}{P}{suffix}";F[N]=D._replace_sprite_label_line(B,Q);G+=1;I+=1
                if H and G>0:
                        with open(J,_D,encoding=_F,newline='')as E:E.writelines(F)
                return G,H
        def _replace_sprite_library_category(B,sprite_lib_path,category_name,atlas_guid,name_to_id,atlas_base,atlas_series):
                Y=name_to_id;X=atlas_guid;R=sprite_lib_path;K=atlas_series
                with open(R,_G,encoding=_F,errors=_H,newline='')as S:L=S.readlines()
                T=0;Z=0;U=_B;a=_B;E=_B;I=_B;D=_A;A=_A;M=X;V=_B;F=_B;e=B._find_assets_root(R);b={};C=0
                while C<len(L):
                        G=L[C];N=G.strip()
                        if N==_Q:a=_C;E=_B;I=_B;D=_A;F=_B;C+=1;continue
                        if a and G.startswith(_M):
                                f=G.split(_E,1)[1].strip();E=f==category_name
                                if E:U=_C
                                I=_B;D=_A;F=_B;C+=1;continue
                        if E and N==_T:I=_C;D=_A;F=_B;C+=1;continue
                        if E and I and G.startswith(_R):
                                D=B._normalize_entry_name(G.split(_E,1)[1].strip());A=_A;M=X
                                if D.isdigit()and K:
                                        H,g=B._map_label_to_atlas(int(D),K)
                                        if H:
                                                M=H[_N];A=B._match_entry_name_to_id(str(g),H[_S],H[_AE])
                                                if A is _A:A=B._match_entry_name_to_id(D,H[_S],H[_AE])
                                if A is _A:A=B._match_entry_name_to_id(D,Y,atlas_base)
                                V=_B;F=_B;C+=1;continue
                        if E and I and D:
                                O=_A
                                if N.startswith(_X):O=_r
                                elif N.startswith(_W):O=_s
                                if O:
                                        if A is _A:
                                                c,P=B._parse_sprite_ref_line(G)
                                                if c and P:
                                                        J=b.get(P)
                                                        if J is _A:
                                                                d=B._find_meta_by_guid(e,P)
                                                                if d:J=B._build_fileid_to_name(d)
                                                                else:J={}
                                                                b[P]=J
                                                        Q=J.get(c)
                                                        if Q:
                                                                if K:
                                                                        for W in K:
                                                                                if Q in W[_S]:A=W[_S][Q];M=W[_N];break
                                                                if A is _A:A=Y.get(Q)
                                        if A is _A:
                                                if not V:Z+=1;V=_C
                                                C+=1;continue
                                        C=B._update_sprite_ref_line(L,C,O,A,M)
                                        if not F:T+=1;F=_C
                                        continue
                        C+=1
                if U and T>0:
                        with open(R,_D,encoding=_F,newline='')as S:S.writelines(L)
                return T,Z,U
        def _update_sprite_ref_line(J,lines,index,key,file_id,guid):
                K='type:';G=guid;F=file_id;E=key;B=lines;A=index;L=B[A];C,H=J._split_line_ending(L);I=C[:len(C)-len(C.lstrip())]
                if K in C:B[A]=f"{I}{E}: {{fileID: {F}, guid: {G}, type: 3}}{H}";return A+1
                if A+1<len(B):
                        M=B[A+1];D,N=J._split_line_ending(M)
                        if D.strip().startswith(K):O=D[:len(D)-len(D.lstrip())];B[A]=f"{I}{E}: {{fileID: {F}, guid: {G},{H}";B[A+1]=f"{O}type: 3}}{N}";return A+2
                B[A]=f"{I}{E}: {{fileID: {F}, guid: {G}, type: 3}}{H}";return A+1
        def _split_line_ending(B,line):
                A=line
                if A.endswith(_AD):return A[:-2],_AD
                if A.endswith(_q):return A[:-1],_q
                return A,''
        def _normalize_entry_name(B,name):
                A=name
                if A.startswith('"')and A.endswith('"')and len(A)>=2:A=A[1:-1]
                return A.strip()
        def _replace_sprite_label_line(B,line,new_label):
                D='m_Name:';C,E=B._split_line_ending(line)
                if D not in C:return line
                F,A=C.split(D,1);A=A.strip();G=B._format_sprite_label(new_label,A);return f"{F}m_Name: {G}{E}"
        def _format_sprite_label(B,label,existing_label):
                C=existing_label;A=label
                if C.startswith('"')and C.endswith('"'):return B._quote_yaml_double(A)
                if C.startswith("'")and C.endswith("'"):return B._quote_yaml_single(A)
                if B._sprite_label_needs_quotes(A):return B._quote_yaml_double(A)
                return A
        def _sprite_label_needs_quotes(B,label):
                A=label
                if A==''or A!=A.strip():return _C
                if A.startswith(('-','?','!','&','*','@')):return _C
                if any(B in A for B in(_E,'#',_q,'\r','\t')):return _C
                if A.lower()in('null','true','false','yes','no','on','off','~'):return _C
                return _B
        def _quote_yaml_double(B,value):A=value.replace('\\','\\\\').replace('"','\\"');return f'"{A}"'
        def _quote_yaml_single(B,value):A=value.replace("'","''");return f"'{A}'"
        def _match_entry_name_to_id(K,entry_name,name_to_id,atlas_base):
                B=entry_name;A=name_to_id
                if B in A:return A[B]
                if B.isdigit():
                        D=(atlas_base or'').strip()
                        if D:
                                C=f"{D}_{B}"
                                if C in A:return A[C]
                                C=f"{D}{B}"
                                if C in A:return A[C]
                        I=f"_{B}",f"-{B}",f" {B}";F=[A for A in A if A.endswith(I)]
                        if len(F)==1:return A[F[0]]
                        J=B.lstrip('0')or'0';E=[]
                        for G in A:
                                H=G.rsplit('_',1)[-1]
                                if H.isdigit()and H.lstrip('0')==J:E.append(G)
                        if len(E)==1:return A[E[0]]
        def _build_atlas_series(F,atlas_file):
                D=Path(atlas_file);N=D.stem;G=re.match(_AO,N)
                if not G:return
                H,I=G.groups();B=int(I);J=len(I);O=D.suffix;P=D.parent;K=[]
                while _C:
                        if J>1:C=f"{H}{B:0{J}d}"
                        else:C=f"{H}{B}"
                        E=P/f"{C}{O}"
                        if not E.exists():break
                        A=str(E)+_I
                        if not os.path.isfile(A):raise FileNotFoundError(f"Atlas .meta not found:\n{A}")
                        L,M=F._parse_sprite_meta(A)
                        if not L:raise ValueError(f"Atlas .meta missing guid:\n{A}")
                        Q=F._count_numeric_suffixes(M,C);K.append({'num':B,_AE:C,'path':str(E),'meta':A,_N:L,_S:M,_AP:Q});B+=1
                return K or _A
        def _count_numeric_suffixes(G,name_to_id,base):
                D=name_to_id;A=set()
                if base:
                        F=re.compile(f"^{re.escape(base)}[_-]?(\\d+)$")
                        for E in D:
                                B=F.match(E)
                                if B:
                                        C=int(B.group(1))
                                        if C>0:A.add(C)
                if A:return len(A)
                for E in D:
                        B=re.search('(\\d+)$',E)
                        if B:
                                C=int(B.group(1))
                                if C>0:A.add(C)
                return len(A)if A else len(D)
        def _map_label_to_atlas(D,label,atlas_series):
                A=label
                for C in atlas_series:
                        B=C.get(_AP)or 0
                        if B<=0:continue
                        if A<=B:return C,A
                        A-=B
                return _A,_A
        def _find_assets_root(E,asset_path):
                A=Path(asset_path);B=A.parts
                for(C,D)in enumerate(B):
                        if D.lower()=='assets':return str(Path(*B[:C+1]))
                return str(A.parent)
        def _build_guid_to_meta_index(B,assets_root):
                A={};E=list(walk(assets_root,{_I}))
                def F(path):
                        try:
                                with open(path,_G,encoding=_F,errors=_H)as D:
                                        for E in range(20):
                                                A=D.readline()
                                                if not A:break
                                                B=A.strip()
                                                if B.startswith(_A2):
                                                        C=B.split(_E,1)[1].strip()
                                                        if C:return C,path
                                                        break
                        except Exception:return
                G=B._parallel_map(E,F,B._io_workers)
                for C in G:
                        if not C:continue
                        D,H=C
                        if D not in A:A[D]=H
                return A
        def _find_meta_by_guid(E,assets_root,guid):
                C=f"guid: {guid}"
                for A in walk(assets_root,{_I}):
                        try:
                                with open(A,_G,encoding=_F,errors=_H)as D:
                                        for F in range(10):
                                                B=D.readline()
                                                if not B:break
                                                if B.strip()==C:return A
                        except Exception:continue
        def _build_fileid_to_name(D,meta_path):
                F,B=D._parse_sprite_meta(meta_path)
                if not B:return{}
                A={}
                for(E,C)in B.items():
                        if C not in A:A[C]=E
                return A
        def _parse_sprite_ref_line(G,line):
                E='fileID:';B=line
                if E not in B or _A2 not in B or'{'not in B:return _A,_A
                try:F=B.split('{',1)[1].replace('}','')
                except Exception:return _A,_A
                C=_A;D=_A
                for A in F.split(','):
                        A=A.strip()
                        if A.startswith(E):C=A.split(_E,1)[1].strip()
                        elif A.startswith(_A2):D=A.split(_E,1)[1].strip()
                return C,D
def main():A=tk.Tk();AA(A);A.mainloop()
if __name__=='__main__':main()
