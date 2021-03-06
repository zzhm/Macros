using System;
using System.ComponentModel;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using System.Data;			
using System.Data.OleDb;

using SwissAcademic.Citavi;
using SwissAcademic.Citavi.DataExchange;
using SwissAcademic.Citavi.Metadata;
using SwissAcademic.Citavi.Shell;
using SwissAcademic.Collections;

// Implementation of macro editor is preliminary and experimental.
// The Citavi object model is subject to change in future version.


//Make sure you have the Microsoft.ACE.OLEDB.16.0 provider installed, which is part of the Microsoft Access Database Engine 2016 Redistributable:
//https://www.microsoft.com/en-us/download/details.aspx?id=54920
//Otherweise you will receive an error saying "Microsoft.ACE.OLEDB.16.0 provider is not registered on the local machine".

public static class CitaviMacro
{
	public static void Main()
	{
		bool createMissingReference = true;
		ReferencePropertyId targetField = ReferencePropertyId.CustomField1;
		string worksheetNameToImport = ""; //leave empty "" to import first worksheet
		
		if (IsBackupAvailable() == false) return;
		
		Project project = Program.ActiveProjectShell.Project;		
		DataTable dataTable = new DataTable();
		
		
		string filter = SwissAcademic.Resources.FileDialogFilters.Excel;
		string fileName = "";
		string initialDirectory = @"C:\Users\<your name>\Documents";
		
		//IMPORTANT: Make sure you provide the name of the columns as in the first row of your excel sheet
		string columnNameShortTitle = "Kurztitel";		//required - needed to identify a reference in the active project
		string columnNameLabel = "Label";				//required - field content to import
		
		
		//string sheetName in ExcelFetcher.GetWorksheets(_dataExchangeProperty.FileName)
		using (OpenFileDialog dialog = new OpenFileDialog())
		{
			dialog.Filter = filter;
			dialog.InitialDirectory = initialDirectory;
			dialog.Title = "Choose EXCEL file with qutations to import";
			if (dialog.ShowDialog() == DialogResult.OK)
			{
				fileName = dialog.FileName;
			}
			else
			{
				return;
			}
		}
		
		if (string.IsNullOrEmpty(worksheetNameToImport))
		{
			DebugMacro.WriteLine(string.Format("Trying to import first worksheet from '{0}'", fileName));
		}
		else
		{
			DebugMacro.WriteLine(string.Format("Trying to import worksheet '{0}' from '{1}'", worksheetNameToImport, fileName));
		}
		
		//GetExistingSheetName will either confirm the handed-over sheet "Datenbank" 
		//OR just return the first sheet OR empty string if there is no sheet in the workbook
		string sheetName = GetExistingSheetName(worksheetNameToImport, fileName);
		if (string.IsNullOrEmpty(sheetName)) return;
		
		dataTable = Sheet2DataTable(fileName, sheetName, -1);
		if (dataTable == null) 
		{
			DebugMacro.WriteLine("An error occurred: No datatable was populated.");
		}
		else
		{
			DebugMacro.WriteLine("Datatable successfully populated.");
		}
	
		
		//explore columns ...	
		DataColumn columnShortTitle = null; //required
		DataColumn columnLabel = null;
			
		//... and generate pointers to specific columns in DataTable
		foreach (DataColumn col in dataTable.Columns)
		{
			if (columnShortTitle == null && col.ToString() == columnNameShortTitle) 
			{
				columnShortTitle = col;
				continue;
			}
			if (columnLabel == null && col.ToString() == columnNameLabel) 
			{
				columnLabel = col;
				continue;
			}
		} //end inspecting columns
		
		
		
		//if no shorttitle column return
		if (columnShortTitle == null) 
		{
			MessageBox.Show("Could not find required column '" + columnNameShortTitle + "' containing the short title.", "Citavi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}
		
		if (columnLabel == null)
		{
			MessageBox.Show("Could not find required column '" + columnNameLabel + "' containing the label.", "Citavi", MessageBoxButtons.OK, MessageBoxIcon.Warning);
			return;
		}

		

		for (int i = 1; i < dataTable.Rows.Count; i++) 
		{
			
			string shortTitle = dataTable.Rows[i][columnShortTitle].ToString();
			string label = dataTable.Rows[i][columnLabel].ToString();

			
			//Lambdas don't work in here for some reason
			//Reference parentReference = project.References.Find(item => item.ShortTitle == dataTable.Rows[i][columnShortTitle].ToString);
			
	
			if (string.IsNullOrEmpty(shortTitle)) 
			{
				//no ShortTitle provided, no import possible
				DebugMacro.WriteLine(string.Format("Importing row {0} ... {1}", i.ToString(), "impossible as ShortTitle is empty"));
			} 
			else 
			{
				//ShortTitle was provided, let's see if we can locate the reference inside the project
				Reference parentReference = GetReferenceWithShortTitle(shortTitle);
				if (parentReference == null)
				{
					if (createMissingReference)
					{
						DebugMacro.WriteLine(string.Format("Importing row {0} ... {1} '{2}'", i.ToString(), "creating new reference with short title", shortTitle));
						//no such reference, generate it
						parentReference = new Reference(project, ReferenceType.Unknown, shortTitle);
						parentReference.ShortTitle = shortTitle;
						project.References.Add(parentReference);
					}
					else
					{
						DebugMacro.WriteLine(string.Format("Importing row {0} ... {1} '{2}'", i.ToString(), "impossible as no reference exists with short title", shortTitle));
					}

				}
				else
				{
					DebugMacro.WriteLine(string.Format("Importing row {0} ... {1} '{2}'", i.ToString(), "into existing reference with short title", shortTitle));
				}
				
				//we now have a parentReference
				if (parentReference != null && !string.IsNullOrEmpty(label))
				{
					parentReference.SetValue(targetField, label);
				}
			}

			
			
		} //end 
		

		MessageBox.Show("Macro has finished execution.", "Citavi", MessageBoxButtons.OK, MessageBoxIcon.Information);
	} //static void Main()
	
	
	/// <summary>
	/// Returns name of desired worksheet OR first worksheet, if no worksheet with the desired name exists.
	/// Returns empty string, if workbook has no worksheets at all.
	/// </summary>
	/// <param name="desiredSheetName"></param>
	/// <param name="fileName"></param>
	/// <returns></returns>
	private static string GetExistingSheetName(string requestedSheetName, string fileName) 
	{
		
		List<string> sheetNames = ExcelFetcher.GetWorksheets(fileName);
		if (sheetNames.Count == 0) return "";
		
		foreach (string sheetName in sheetNames)
		{
			if (sheetName == requestedSheetName) return sheetName;
		}
		return sheetNames[0];
	}

	private static string GetConnectionString(string fileName)
	{
		string connectionString = string.Empty;
		connectionString = "Provider=Microsoft.ACE.OLEDB.16.0;" +
		"Data Source={0};Extended Properties=" + Convert.ToChar(34).ToString() + "Excel 12.0;HDR=YES;" + Convert.ToChar(34).ToString();
		return string.Format(connectionString, fileName);
	}
	
	private static DataTable Sheet2DataTable(string fileName, string sheetName, int maxRowCount)
	{
		DataTable dataTable = new DataTable();
		OleDbDataReader dataReader = null;
		DataRow row = null;

		string selectString = @"SELECT * FROM ["
								+ "{0}"
								+ "]";

		try
		{
			using (OleDbConnection connection = new OleDbConnection(GetConnectionString(fileName)))
			{
				connection.Open();
				object[] o = new Object[] { null, null, null, "TABLE" };
				using (DataTableReader dataReader2 = connection.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, o).CreateDataReader())
				{
					while (dataReader2.Read())
					{
						string sheetName2 = dataReader2["TABLE_NAME"].ToString();
						if (sheetName2.EndsWith("$") ||
							sheetName2.EndsWith("$_") ||
							sheetName2.EndsWith("$'") ||
							sheetName2.EndsWith("$'_"))
						{
							sheetName2 = sheetName2.Remove(sheetName2.IndexOf("$"));
						}
						if (sheetName2.StartsWith("'"))
						{
							sheetName2 = sheetName2.Remove(0, 1);
						}
						if (sheetName == sheetName2)
						{
							sheetName = dataReader2["TABLE_NAME"].ToString();
							break;
						}
					}
				}
			}
			
			DebugMacro.WriteLine(string.Format("Trying to populate Datatable from Worksheet '{0}'", sheetName));

			using (OleDbConnection connection = new OleDbConnection(GetConnectionString(fileName)))
			{
				connection.Open();
				using (OleDbCommand command = new OleDbCommand(string.Format(selectString, sheetName), connection))
				{
					dataReader = command.ExecuteReader();

					while (dataReader.Read())
					{
						row = dataTable.NewRow();
						for (int i = 0; i < dataReader.FieldCount; i++)
						{
							if (dataTable.Columns.Count == i)
							{
								string name = dataReader.GetName(i);
								if (dataTable.Columns.Contains(name))
								{
									dataTable.Columns.Add("Column" + i.ToString());
								}
								else
								{
									dataTable.Columns.Add(name);
								}
							}
							if (dataReader[i] != DBNull.Value)
							{
								row[i] = dataReader[i].ToString();
							}
							else
							{
								row[i] = string.Empty;
							}
						}
						dataTable.Rows.Add(row);
						maxRowCount--;
						if (maxRowCount == 0)
							break;
					}
				}
			}

			row = dataTable.NewRow();
			foreach (DataColumn column in dataTable.Columns)
			{
				row[column] = column.ColumnName;
			}
			dataTable.Rows.InsertAt(row, 0);
			
			#region Clean

			
			bool isEmpty = true;
			for (int i = 0; i < dataTable.Columns.Count; i++)
			{
				isEmpty = true;
				for (int i1 = 0; i1 < dataTable.Rows.Count; i1++)
				{
					if (!string.IsNullOrEmpty(dataTable.Rows[i1][i].ToString()))
					{
						isEmpty = false;
						i1 = dataTable.Rows.Count;
					}
				}
					if (isEmpty)
				{
					dataTable.Columns.RemoveAt(i);
					i--;
				}
			}
			for (int i = 0; i < dataTable.Rows.Count; i++)
			{
				isEmpty = true;
				for (int i1 = 0; i1 < dataTable.Columns.Count; i1++)
				{
					if (!string.IsNullOrEmpty(dataTable.Rows[i][i1].ToString()))
					{
						isEmpty = false;
						i1 = dataTable.Columns.Count;
					}
				}
					if (isEmpty)
				{
					dataTable.Rows.RemoveAt(i);
					i--;
				}
			}
				#endregion
			}
		catch (Exception ignored)
		{
			MessageBox.Show(ignored.Message);
		}
		finally
		{
			if (dataReader != null && !dataReader.IsClosed)
				dataReader.Close();
		}
		return dataTable;
	}

	
	private static Reference GetReferenceWithShortTitle(string shortTitle)
	{
		Project project = Program.ActiveProjectShell.Project;
		//Reference foundReference = null;
		
		foreach (Reference reference in project.References)
		{
			if (reference.ShortTitle == shortTitle) return reference;				
		}
		return null;
	}
	
	
	private static bool IsBackupAvailable() 
	{
		string warning = String.Concat("CAUTION: This macro is going to make changes to your project.",
			"\n\n", "Pls. ensure that you have made a fresh backup of your project before executing this macro.",
			"\n", "If you haven't done so yet, click 'Cancel' and choose 'Create backup…' from the 'File' menu in the main window of Citavi.",
			"\n\n", "Do you have a backup available and want to continue now with the macro?"
		);
				
		
		return (MessageBox.Show(warning, "Citavi", MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2) == DialogResult.OK);
		//Program.CreateBackup(Program.ActiveProjectShell.PrimaryMainForm); //will work in future versions, > 3.0.2
	
	
	}
	
	
} //public static class CitaviMacro
