from .common import (
    _A, _B, _C, _D, _E, _F, _G, _H, _M, _Q, _R, _T,
    _W, _X, _r, _s,
)


class SpriteLibraryReplaceMixin:
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
