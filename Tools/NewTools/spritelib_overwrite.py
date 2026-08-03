from .common import (
    Path, _A, _A0, _A1, _A9, _AC, _AD, _AE, _AK, _AL,
    _AM, _AN, _AO, _AP, _AX, _B, _C, _D, _E, _F, _G,
    _H, _I, _M, _N, _Q, _R, _S, _T, _U, _V, _W, _X,
    _a, _b, _i, _j, _k, _l, _m, _n, _o, _p, _q, _t,
    _u, _z, os, re, walk, zlib,
)


class SpriteLibraryOverwriteMixin:
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
