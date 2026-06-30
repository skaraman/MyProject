from .common import (
    _A, _A4, _A5, _A6, _A7, _A8, _A9, _AA, _AB, _AI,
    _AJ, _AK, _AL, _AM, _AN, _AR, _AS, _AT, _AU, _AV,
    _AW, _B, _C, _D, _E, _F, _G, _H, _I, _K, _M, _O,
    _P, _Q, _R, _T, _U, _Y, _Z, _a, _c, _g, _h, _i,
    _j, _k, _l, _m, _n, _o, _p, _t, _u, _y, _z,
    filedialog, messagebox, os, tk, ttk,
)


class SpriteLibraryUiMixin:
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
