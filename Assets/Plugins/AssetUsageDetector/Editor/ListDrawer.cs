using System.Collections.Generic;
using UnityEditor;
using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AssetUsageDetectorNamespace
{
	public abstract class ListDrawer<T>
	{
		private readonly string label;
		private readonly bool acceptSceneObjects;

		protected ListDrawer( string label, bool acceptSceneObjects )
		{
			this.label = label;
			this.acceptSceneObjects = acceptSceneObjects;
		}

		// Exposes a list on GUI
		public bool Draw( List<T> list )
		{
			bool hasChanged = false;
			bool guiEnabled = GUI.enabled;

			Event ev = Event.current;

			GUILayout.BeginHorizontal();

			GUILayout.Label( label );

			if( guiEnabled )
			{
				// Handle drag & drop references to array
				// Credit: https://answers.unity.com/answers/657877/view.html
				if( ( ev.type == EventType.DragPerform || ev.type == EventType.DragUpdated ) && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
				{
					DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
					if( ev.type == EventType.DragPerform )
					{
						DragAndDrop.AcceptDrag();

						Object[] draggedObjects = DragAndDrop.objectReferences;
						if( draggedObjects.Length > 0 )
						{
							for( int i = 0; i < draggedObjects.Length; i++ )
							{
								if( draggedObjects[i] != null && !draggedObjects[i].Equals( null ) )
								{
									bool replacedNullElement = false;
									for( int j = 0; j < list.Count; j++ )
									{
										if( IsElementNull( list[j] ) )
										{
											list[j] = CreateElement( draggedObjects[i] );

											replacedNullElement = true;
											break;
										}
									}

									if( !replacedNullElement )
										list.Add( CreateElement( draggedObjects[i] ) );

									hasChanged = true;
								}
							}
						}
					}

					ev.Use();
				}
				else if( ev.type == EventType.ContextClick && GUILayoutUtility.GetLastRect().Contains( ev.mousePosition ) )
				{
					GenericMenu contextMenu = new GenericMenu();
					contextMenu.AddItem( new GUIContent( "Clear" ), false, () =>
					{
						list.Clear();
						list.Add( CreateElement( null ) );
					} );
					contextMenu.ShowAsContext();

					ev.Use();
				}

				if( GUILayout.Button( "+", Utilities.GL_WIDTH_25 ) )
					list.Insert( 0, CreateElement( null ) );
			}

			GUILayout.EndHorizontal();

			for( int i = 0; i < list.Count; i++ )
			{
				T element = list[i];

				GUI.changed = false;
				GUILayout.BeginHorizontal();

				Object prevObject = GetObjectFromElement( element );
				Object newObject = EditorGUILayout.ObjectField( "", prevObject, typeof( Object ), acceptSceneObjects );

				if( GUI.changed )
				{
					hasChanged = true;
					SetObjectOfElement( list, i, newObject );
				}

				if( guiEnabled )
				{
					if( GUILayout.Button( "+", Utilities.GL_WIDTH_25 ) )
						list.Insert( i + 1, CreateElement( null ) );

					if( GUILayout.Button( "-", Utilities.GL_WIDTH_25 ) )
					{
						if( element != null && !element.Equals( null ) )
							hasChanged = true;

						// Lists with no elements look ugly, always keep a dummy null variable
						if( list.Count > 1 )
							list.RemoveAt( i-- );
						else
							list[0] = CreateElement( null );
					}
				}

				GUILayout.EndHorizontal();

				PostElementDrawer( element );
			}

			return hasChanged;
		}

		protected abstract T CreateElement( Object source );
		protected abstract Object GetObjectFromElement( T element );
		protected abstract void SetObjectOfElement( List<T> list, int index, Object value );
		protected abstract bool IsElementNull( T element );
		protected abstract void PostElementDrawer( T element );
	}

	public class ObjectListDrawer : ListDrawer<Object>
	{
		public ObjectListDrawer( string label, bool acceptSceneObjects ) : base( label, acceptSceneObjects )
		{
		}

		protected override Object CreateElement( Object source )
		{
			return source;
		}

		protected override Object GetObjectFromElement( Object element )
		{
			return element;
		}

		protected override void SetObjectOfElement( List<Object> list, int index, Object value )
		{
			list[index] = value;
		}

		protected override bool IsElementNull( Object element )
		{
			return element == null || element.Equals( null );
		}

		protected override void PostElementDrawer( Object element )
		{
		}
	}

	public class ObjectToSearchListDrawer : ListDrawer<ObjectToSearch>
	{
		public ObjectToSearchListDrawer() : base( "Find references of:", true )
		{
		}

		protected override ObjectToSearch CreateElement( Object source )
		{
			return new ObjectToSearch( source );
		}

		protected override Object GetObjectFromElement( ObjectToSearch element )
		{
			return element.obj;
		}

		protected override void SetObjectOfElement( List<ObjectToSearch> list, int index, Object value )
		{
			list[index].obj = value;
			list[index].RefreshSubAssets();
		}

		protected override bool IsElementNull( ObjectToSearch element )
		{
			return element == null || element.obj == null || element.obj.Equals( null );
		}

		protected override void PostElementDrawer( ObjectToSearch element )
		{
			if( element.obj is MonoScript )
				element.RefreshScriptFieldFilters();

			List<ObjectToSearch.SubAsset> subAssetsToSearch = element.subAssets;
			if( subAssetsToSearch.Count > 0 )
			{
				GUILayout.BeginHorizontal();

				// 0-> all toggles off, 1-> mixed, 2-> all toggles on
				bool toggleAllSubAssets = subAssetsToSearch[0].shouldSearch;
				bool mixedToggle = false;
				for( int j = 1; j < subAssetsToSearch.Count; j++ )
				{
					if( subAssetsToSearch[j].shouldSearch != toggleAllSubAssets )
					{
						mixedToggle = true;
						break;
					}
				}

				if( mixedToggle )
					EditorGUI.showMixedValue = true;

				GUI.changed = false;
				toggleAllSubAssets = EditorGUILayout.Toggle( toggleAllSubAssets, Utilities.GL_WIDTH_25 );
				if( GUI.changed )
				{
					for( int j = 0; j < subAssetsToSearch.Count; j++ )
						subAssetsToSearch[j].shouldSearch = toggleAllSubAssets;
				}

				EditorGUI.showMixedValue = false;

				element.showSubAssetsFoldout = EditorGUILayout.Foldout( element.showSubAssetsFoldout, "Include sub-assets in search:", true );

				GUILayout.EndHorizontal();

				if( element.showSubAssetsFoldout )
				{
					for( int j = 0; j < subAssetsToSearch.Count; j++ )
					{
						GUILayout.BeginHorizontal();

						subAssetsToSearch[j].shouldSearch = EditorGUILayout.Toggle( subAssetsToSearch[j].shouldSearch, Utilities.GL_WIDTH_25 );

						bool guiEnabled = GUI.enabled;
						GUI.enabled = false;
						EditorGUILayout.ObjectField( string.Empty, subAssetsToSearch[j].subAsset, typeof( Object ), true );
						GUI.enabled = guiEnabled;

						GUILayout.EndHorizontal();
					}
				}
			}

			List<ObjectToSearch.ScriptFieldFilter> scriptFieldFilters = element.scriptFieldFilters;
			if( scriptFieldFilters != null && scriptFieldFilters.Count > 0 )
			{
				GUILayout.BeginHorizontal();
				GUILayout.Space( 6f );
				element.showScriptFieldFiltersFoldout = EditorGUILayout.Foldout( element.showScriptFieldFiltersFoldout, "Filter script fields:", true );
				GUILayout.EndHorizontal();

				if( element.showScriptFieldFiltersFoldout )
				{
					EditorGUILayout.HelpBox( "Checked fields are required. String/numeric filters are partial matches. For Object/Text fields, checked + empty means the field must be empty.", MessageType.None );

					for( int i = 0; i < scriptFieldFilters.Count; i++ )
					{
						ObjectToSearch.ScriptFieldFilter fieldFilter = scriptFieldFilters[i];
						Type fieldType = fieldFilter.EffectiveFieldType;

						GUILayout.BeginHorizontal();
						fieldFilter.shouldFilter = EditorGUILayout.Toggle( fieldFilter.shouldFilter, Utilities.GL_WIDTH_25 );
						if( fieldFilter.IsObjectReference )
							fieldFilter.value = EditorGUILayout.ObjectField( fieldFilter.Label, fieldFilter.value, fieldFilter.FieldType, true );
						else if( fieldType == typeof( string ) )
							fieldFilter.stringValue = EditorGUILayout.TextField( fieldFilter.Label, fieldFilter.stringValue ?? string.Empty );
						else if( fieldType == typeof( bool ) )
							fieldFilter.boolValue = EditorGUILayout.Toggle( fieldFilter.Label, fieldFilter.boolValue );
						else if( fieldType == typeof( float ) )
							fieldFilter.floatValue = EditorGUILayout.FloatField( fieldFilter.Label, fieldFilter.floatValue );
						else if( fieldType == typeof( double ) )
							fieldFilter.doubleValue = EditorGUILayout.DoubleField( fieldFilter.Label, fieldFilter.doubleValue );
						else if( IsIntegralFieldType( fieldType ) )
							fieldFilter.longValue = EditorGUILayout.LongField( fieldFilter.Label, fieldFilter.longValue );
						else if( fieldType.IsEnum )
						{
							string[] enumNames = Enum.GetNames( fieldType );
							if( enumNames.Length > 0 )
							{
								int selectedIndex = Array.IndexOf( enumNames, fieldFilter.enumValueName );
								if( selectedIndex < 0 )
									selectedIndex = 0;

								selectedIndex = EditorGUILayout.Popup( fieldFilter.Label, selectedIndex, enumNames );
								fieldFilter.enumValueName = enumNames[selectedIndex];
							}
							else
								EditorGUILayout.LabelField( fieldFilter.Label, "<empty enum>" );
						}
						else
							fieldFilter.textValue = EditorGUILayout.TextField( fieldFilter.Label, fieldFilter.textValue ?? string.Empty );
						GUILayout.EndHorizontal();
					}
				}
			}
		}

		private static bool IsIntegralFieldType( Type type )
		{
			return type == typeof( byte ) || type == typeof( sbyte ) || type == typeof( short ) || type == typeof( ushort ) ||
				   type == typeof( int ) || type == typeof( uint ) || type == typeof( long );
		}
	}
}
