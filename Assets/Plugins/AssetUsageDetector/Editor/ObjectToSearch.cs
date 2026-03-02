using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;
using Object = UnityEngine.Object;

namespace AssetUsageDetectorNamespace
{
	[Serializable]
	public class ObjectToSearch
	{
		[Serializable]
		public class ScriptFieldFilter
		{
			[NonSerialized]
			private Type m_fieldType;

			[NonSerialized]
			private bool m_fieldTypeInitialized;

			public string declaringTypeName;
			public string fieldName;
			public string fieldTypeName;
			public bool shouldFilter;
			public Object value;
			public string stringValue;
			public bool boolValue;
			public long longValue;
			public float floatValue;
			public double doubleValue;
			public string enumValueName;
			public string textValue;

			public string FieldKey
			{
				get { return string.Concat( declaringTypeName, ".", fieldName ); }
			}

			public Type FieldType
			{
				get
				{
					if( !m_fieldTypeInitialized )
					{
						m_fieldTypeInitialized = true;

						if( !string.IsNullOrEmpty( fieldTypeName ) )
							m_fieldType = Type.GetType( fieldTypeName );

						if( m_fieldType == null )
							m_fieldType = typeof( string );
					}

					return m_fieldType;
				}
			}

			public bool IsObjectReference
			{
				get { return typeof( Object ).IsAssignableFrom( FieldType ); }
			}

			public Type EffectiveFieldType
			{
				get { return Nullable.GetUnderlyingType( FieldType ) ?? FieldType; }
			}

			public string Label
			{
				get
				{
					string displayName = ObjectNames.NicifyVariableName( fieldName );
					Type fieldType = FieldType;
					return fieldType != null && fieldType != typeof( Object ) ? string.Concat( displayName, " (", fieldType.Name, ")" ) : displayName;
				}
			}

			public ScriptFieldFilter( string declaringTypeName, string fieldName, string fieldTypeName )
			{
				this.declaringTypeName = declaringTypeName;
				this.fieldName = fieldName;
				this.fieldTypeName = fieldTypeName;
				shouldFilter = false;
				value = null;
				stringValue = string.Empty;
				boolValue = false;
				longValue = 0L;
				floatValue = 0f;
				doubleValue = 0d;
				enumValueName = string.Empty;
				textValue = string.Empty;
			}
		}

		[Serializable]
		public class SubAsset
		{
			public Object subAsset;
			public bool shouldSearch;

			public SubAsset( Object subAsset, bool shouldSearch )
			{
				this.subAsset = subAsset;
				this.shouldSearch = shouldSearch;
			}
		}

		public Object obj;
		public List<SubAsset> subAssets;
		public bool showSubAssetsFoldout;
		public List<ScriptFieldFilter> scriptFieldFilters;
		public bool showScriptFieldFiltersFoldout;

		private static HashSet<Object> currentSubAssets;

		public ObjectToSearch( Object obj, bool? shouldSearchChildren = null )
		{
			this.obj = obj;
			RefreshSubAssets( shouldSearchChildren );
		}

		public void RefreshSubAssets( bool? shouldSearchChildren = null )
		{
			if( subAssets == null )
				subAssets = new List<SubAsset>();
			else
				subAssets.Clear();

			if( currentSubAssets == null )
				currentSubAssets = new HashSet<Object>();
			else
				currentSubAssets.Clear();

			AddSubAssets( obj, false, shouldSearchChildren );
			currentSubAssets.Clear();

			RefreshScriptFieldFilters();
		}

		private void AddSubAssets( Object target, bool includeTarget, bool? shouldSearchChildren )
		{
			if( target == null || target.Equals( null ) )
				return;

			if( !target.IsAsset() )
			{
				GameObject go = target as GameObject;
				if( !go || !go.scene.IsValid() )
					return;

				// If this is a scene object, add its child objects to the sub-assets list
				// but don't include them in the search by default
				Transform goTransform = go.transform;
				Transform[] children = go.GetComponentsInChildren<Transform>( true );
				for( int i = 0; i < children.Length; i++ )
				{
					if( ReferenceEquals( children[i], goTransform ) )
						continue;

					subAssets.Add( new SubAsset( children[i].gameObject, shouldSearchChildren ?? false ) );
				}
			}
			else
			{
				if( !AssetDatabase.IsMainAsset( target ) || target is SceneAsset )
					return;

				if( includeTarget )
				{
					if( currentSubAssets.Add( target ) )
						subAssets.Add( new SubAsset( target, shouldSearchChildren ?? true ) );
				}
				else
				{
					// If asset is a directory, add all of its contents as sub-assets recursively
					if( target.IsFolder() )
					{
						foreach( string filePath in Utilities.EnumerateFolderContents( target ) )
							AddSubAssets( AssetDatabase.LoadAssetAtPath<Object>( filePath ), true, shouldSearchChildren );

						return;
					}
				}

				// Add Sprites of SpriteAtlases to the sub-assets list
				if( target is SpriteAtlas spriteAtlas )
				{
					Sprite[] packedSprites = AssetUsageDetector.spriteAtlasPackedSpritesGetter( spriteAtlas );
					if( packedSprites != null )
					{
						for( int i = 0; i < packedSprites.Length; i++ )
						{
							if( packedSprites[i] != null && currentSubAssets.Add( packedSprites[i] ) )
								subAssets.Add( new SubAsset( packedSprites[i], shouldSearchChildren ?? true ) );
						}
					}
				}

				// Find sub-asset(s) of the asset (if any)
				Object[] assets = AssetDatabase.LoadAllAssetsAtPath( AssetDatabase.GetAssetPath( target ) );
				for( int i = 0; i < assets.Length; i++ )
				{
					Object asset = assets[i];
					if( asset == null || asset.Equals( null ) || asset is Component || asset == target )
						continue;

					// Nested prefabs in prefab assets add an additional native object of type 'UnityEngine.PrefabInstance' to the prefab. Managed type of that native type
					// is UnityEngine.Object (i.e. GetType() returns UnityEngine.Object, not UnityEngine.PrefabInstance). There are no possible references to these native
					// objects so skip them (we're checking for UnityEngine.Prefab because it includes other native types like UnityEngine.PrefabCreation, as well)
					if( target is GameObject && asset.GetType() == typeof( Object ) && asset.ToString().Contains( "(UnityEngine.Prefab" ) )
						continue;

					if( currentSubAssets.Add( asset ) )
						subAssets.Add( new SubAsset( asset, shouldSearchChildren ?? true ) );
				}
			}
		}

		public void RefreshScriptFieldFilters()
		{
			Dictionary<string, ScriptFieldFilter> previousFilters = null;
			if( scriptFieldFilters == null )
				scriptFieldFilters = new List<ScriptFieldFilter>();
			else if( scriptFieldFilters.Count > 0 )
			{
				previousFilters = new Dictionary<string, ScriptFieldFilter>( scriptFieldFilters.Count );
				for( int i = 0; i < scriptFieldFilters.Count; i++ )
					previousFilters[scriptFieldFilters[i].FieldKey] = scriptFieldFilters[i];

				scriptFieldFilters.Clear();
			}

			MonoScript script = obj as MonoScript;
			if( !script )
				return;

			Type scriptType = script.GetClass();
			if( scriptType == null || ( !typeof( MonoBehaviour ).IsAssignableFrom( scriptType ) && !typeof( ScriptableObject ).IsAssignableFrom( scriptType ) ) )
				return;

			MonoImporter scriptImporter = AssetImporter.GetAtPath( AssetDatabase.GetAssetPath( script ) ) as MonoImporter;

			List<Type> typeHierarchy = new List<Type>( 4 );
			for( Type type = scriptType; type != null && type != typeof( object ); type = type.BaseType )
				typeHierarchy.Add( type );

			for( int i = typeHierarchy.Count - 1; i >= 0; i-- )
			{
				FieldInfo[] fields = typeHierarchy[i].GetFields( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly );
				for( int j = 0; j < fields.Length; j++ )
				{
					FieldInfo field = fields[j];
					if( !IsScriptFieldFilterCandidate( field ) )
						continue;

					ScriptFieldFilter filter = new ScriptFieldFilter( field.DeclaringType.AssemblyQualifiedName ?? field.DeclaringType.FullName, field.Name, field.FieldType.AssemblyQualifiedName );
					if( previousFilters != null && previousFilters.TryGetValue( filter.FieldKey, out ScriptFieldFilter previousFilter ) )
					{
						filter.shouldFilter = previousFilter.shouldFilter;
						filter.value = previousFilter.value;
						filter.stringValue = previousFilter.stringValue;
						filter.boolValue = previousFilter.boolValue;
						filter.longValue = previousFilter.longValue;
						filter.floatValue = previousFilter.floatValue;
						filter.doubleValue = previousFilter.doubleValue;
						filter.enumValueName = previousFilter.enumValueName;
						filter.textValue = previousFilter.textValue;
					}
					else if( scriptImporter != null && filter.IsObjectReference )
						filter.value = scriptImporter.GetDefaultReference( field.Name );

					if( string.IsNullOrEmpty( filter.enumValueName ) )
					{
						Type fieldType = filter.EffectiveFieldType;
						if( fieldType.IsEnum )
						{
							string[] enumNames = Enum.GetNames( fieldType );
							if( enumNames.Length > 0 )
								filter.enumValueName = enumNames[0];
						}
					}

					scriptFieldFilters.Add( filter );
				}
			}
		}

		private static bool IsScriptFieldFilterCandidate( FieldInfo field )
		{
			if( field == null || field.IsStatic )
				return false;

			if( field.IsInitOnly || field.IsLiteral )
				return false;

			if( Attribute.IsDefined( field, typeof( ObsoleteAttribute ) ) )
				return false;

			Type fieldType = field.FieldType;
			if( fieldType.IsPointer || fieldType.IsByRef )
				return false;

			if( field.IsPublic )
				return !Attribute.IsDefined( field, typeof( NonSerializedAttribute ) );

			return Attribute.IsDefined( field, typeof( SerializeField ), true );
		}
	}
}
