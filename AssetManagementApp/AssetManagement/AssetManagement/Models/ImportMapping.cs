namespace AssetManagement.Models
{
    public static class ImportMapping
    {
        public static Dictionary<string, List<string>> Tables = new()
        {
            {
                "SECTION",
                new List<string>
                {
                    "OBJ_PK",
                    "OBJ_Key",
                    "OBJ_Size1",
                    "OBJ_Material",
                    "OBJ_Spare4",
                    "OBJ_Spare5"
                }
            },
            {
                "SECINSP",
                new List<string>
                {
                    "INS_PK",
                    "INS_Section_FK",
                    "INS_StartDate",
                    "INSP_InspectedLength"
                }
            },
            {
                "SECOBS",
                new List<string>
                {
                    "OBS_PK",
                    "OBS_Inspection_FK",
                    "OBS_Distance",
                    "OBS_Observation",
                    "OBS_GradeS",
                    "OBS_ScoreS"
                }
            },
            {
                "SECOBSMM",
                new List<string>
                {
                    "OMM_PK",
                    "OMM_Observation_FK",
                    "OMM_FileName",
                    "OMM_FileType"
                }
            },
            {
                "SECSTAT",
                new List<string>
                {
                    "STA_PK",
                    "STA_Inspection_FK",
                    "STA_Type",
                    "STA_HighestGrade",
                    "STA_TotalScore",
                    "STA_PeakScore"
                }
            }
        };
    }
}

