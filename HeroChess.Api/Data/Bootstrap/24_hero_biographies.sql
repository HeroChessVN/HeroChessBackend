-- Fill missing Vietnamese biographies after the hero catalog has been seeded.
BEGIN;

WITH biographies(code, biography) AS (
    VALUES
    -- https://baotanglichsu.vn/vi/Articles/3098/13485/cuoc-khoi-nghia-hai-ba-trung-nam-40-43-sau-cong-nguyen.html
    -- https://baotanglichsu.vn/vi/Articles/2001/65263/quoc-hieu-viet-nam-thoi-trung-vuong.html
    ('trung-trac', $bio$Trưng Trắc xuất thân từ gia đình Lạc tướng ở Mê Linh. Năm 40, bà cùng em gái Trưng Nhị phát động khởi nghĩa chống chính quyền đô hộ Đông Hán do Tô Định đứng đầu. Sau khi nghĩa quân giành được nhiều thành trì, bà lên ngôi, xưng là Trưng Vương và đóng đô ở Mê Linh.

Nhà Đông Hán sai Mã Viện đem quân sang đàn áp. Trưng Trắc tiếp tục lãnh đạo cuộc kháng cự cùng Trưng Nhị cho đến năm 43. Các đền thờ ở Mê Linh và Hát Môn là những nơi tưởng niệm Hai Bà Trưng.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/3098/13485/cuoc-khoi-nghia-hai-ba-trung-nam-40-43-sau-cong-nguyen.html
    -- https://sknc.qdnd.vn/chuyen-xua-nay/toa-sang-khi-tiet-hai-ba-trung-502900
    ('trung-nhi', $bio$Trưng Nhị là em gái của Trưng Trắc. Năm 40, bà cùng chị tập hợp lực lượng ở vùng Mê Linh và lãnh đạo cuộc khởi nghĩa chống Đông Hán. Nghĩa quân tiến đánh Luy Lâu, trung tâm cai trị của nhà Đông Hán ở Giao Chỉ, rồi giành quyền tự chủ.

Khi quân của Mã Viện tiến đến Lãng Bạc, Trưng Nhị cùng Trưng Vương đem quân từ Mê Linh ra giao chiến. Cuộc kháng cự kéo dài đến năm 43. Hậu thế thờ Trưng Nhị cùng chị trong các đền thờ Hai Bà Trưng.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/16192/ly-thuong-kiet-nguoi-chi-huy-kiet-xuat-cua-cuoc-khang-chien-chong-quan-tong-lan-thu-hai.html
    ('ly-thuong-kiet-xe', $bio$Lý Thường Kiệt là danh tướng triều Lý, từng nắm giữ những trọng trách về quân sự và chính trị của Đại Việt thế kỷ XI. Ông tham gia chiến dịch đánh Chiêm Thành năm 1069 và là người chỉ huy chủ chốt trong cuộc kháng chiến chống quân Tống lần thứ hai.

Năm 1075, ông chỉ huy quân Đại Việt tiến công các căn cứ của nhà Tống ở Ung, Khâm và Liêm. Khi quân Tống tiến vào Đại Việt, ông tổ chức phòng tuyến trên sông Như Nguyệt. Cuộc kháng chiến kết thúc thắng lợi năm 1077.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/14318/thai-su-tran-thu-djo.html
    -- https://baotanglichsu.vn/VI/Articles/3096/12006/tran-djanh-kinh-djong-ca-the-gioi-cua-nguoi-viet.html
    ('tran-thu-do', $bio$Trần Thủ Độ là một trong những nhân vật có ảnh hưởng lớn trong giai đoạn chuyển giao quyền lực từ nhà Lý sang nhà Trần. Năm 1224, ông giữ chức Điện tiền Chỉ huy sứ. Sau khi nhà Trần được thành lập, ông trở thành Thái sư và nắm vai trò lãnh đạo quan trọng trong triều đình trong nhiều thập niên. Ông tham gia tổ chức lại bộ máy chính quyền và củng cố vương triều Trần trong giai đoạn đầu.

Trần Thủ Độ cũng có vai trò trong cuộc kháng chiến chống quân Mông Cổ lần thứ nhất năm 1258. Khi tình hình chiến sự khó khăn, sử liệu chép rằng ông khuyên vua Trần Thái Tông giữ vững ý chí kháng chiến.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/2001/65033/mot-so-vi-vua-chua-cam-tinh-con-chuot-trong-lich-su-viet-nam.html
    -- https://scov.gov.vn/dat-nuoc-con-nguoi/tom-tat-bien-nien-su-vn/xiv.-nha-tran-175-nam-1225-1400-quoc-hieu-dai-viet-kinh-do-thang-long-phan-1-.html
    -- https://baotanglichsu.vn/VI/Articles/3096/12006/tran-djanh-kinh-djong-ca-the-gioi-cua-nguoi-viet.html
    ('tran-thanh-tong', $bio$Trần Thánh Tông (1240–1290), tên húy Trần Hoảng, là con của Trần Thái Tông và là vị vua thứ hai của nhà Trần. Khi còn là thái tử, ông cùng vua cha phản công quân Mông Cổ ở Đông Bộ Đầu năm 1258. Ông lên ngôi trong năm đó và trị vì đến năm 1278.

Sau khi nhường ngôi cho Trần Nhân Tông, ông làm Thái thượng hoàng và cùng vua con lãnh đạo hai cuộc kháng chiến chống quân Nguyên năm 1285 và 1287–1288. Thơ của ông còn được lưu lại trong Đại Việt sử ký toàn thư và Việt âm thi tập.$bio$),

    -- https://scov.gov.vn/dat-nuoc-con-nguoi/tom-tat-bien-nien-su-vn/xiv.-nha-tran-175-nam-1225-1400-quoc-hieu-dai-viet-kinh-do-thang-long-phan-1-.html
    -- https://baotanglichsu.vn/VI/Articles/3096/71341/tran-thuy-chien-bach-djang-nhung-manh-ghep.html
    ('tran-hung-dao-tuong', $bio$Trần Hưng Đạo, tên thật Trần Quốc Tuấn, là một trong những tướng lĩnh quan trọng của nhà Trần trong các cuộc kháng chiến chống quân Mông – Nguyên thế kỷ XIII. Ông là con của An Sinh Vương Trần Liễu và thuộc hoàng tộc nhà Trần. Đặc biệt trong hai cuộc kháng chiến năm 1285 và 1287–1288, ông giữ vai trò Quốc công Tiết chế, thống lĩnh các lực lượng quân sự của Đại Việt. Chiến thắng Bạch Đằng năm 1288 là một trong những dấu ấn lớn trong sự nghiệp cầm quân của ông.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/18629/chieu-van-vuong-tran-nhat-duat.html
    ('tran-nhat-duat', $bio$Trần Nhật Duật là con thứ sáu của vua Trần Thái Tông và Hoàng hậu Thuận Thiên, đồng thời là em của vua Trần Thánh Tông và Trần Quang Khải. Ông nổi tiếng là người có học vấn rộng, am hiểu chính trị, quân sự, ngôn ngữ và phong tục của nhiều dân tộc. Bảo tàng Lịch sử Quốc gia ghi nhận ông biết tiếng Hoa, tiếng Chiêm Thành và tiếng của người Sách Mã Tích.

Một sự kiện tiêu biểu thể hiện khả năng ngoại giao của Trần Nhật Duật diễn ra năm 1280, khi ông được giao giải quyết việc Trịnh Giác Mật chống lại triều đình tại Đà Giang. Nhờ hiểu phong tục và cách giao tiếp của địa phương, ông thuyết phục được Trịnh Giác Mật quy thuận mà không phải tiến hành một cuộc giao tranh lớn.

Trong cuộc kháng chiến chống Nguyên–Mông lần thứ hai năm 1285, Trần Nhật Duật là một trong những tướng lĩnh quan trọng của nhà Trần. Ông trực tiếp chỉ huy lực lượng trong trận Hàm Tử, góp phần vào cuộc phản công và thắng lợi chung của quân dân Đại Việt. Ông cũng tiếp tục tham gia cuộc kháng chiến lần thứ ba năm 1288.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/17651/chieu-minh-vuong-tran-quang-khai.html
    ('tran-quang-khai', $bio$Trần Quang Khải là con vua Trần Thái Tông và là một trong những nhân vật quan trọng của triều Trần thế kỷ XIII. Ông mang tước Chiêu Minh Vương, giữ chức Tướng quốc Thái úy và về sau làm Thượng tướng. Ông được ghi nhận là người có kiến thức rộng, giỏi cả văn lẫn võ và am hiểu một số ngôn ngữ của các dân tộc khác.

Trong cuộc kháng chiến chống Nguyên – Mông lần thứ hai năm 1285, Trần Quang Khải giữ vai trò quan trọng trong cuộc phản công của quân Trần. Ông cùng các tướng lĩnh chỉ huy quân đánh thắng tại Chương Dương, góp phần tạo điều kiện giải phóng kinh thành Thăng Long.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/2001/66032/cay-duoi-djai-thu-va-chung-tich-hoi-nghi-binh-than.html
    -- https://baotanglichsu.vn/VI/Articles/3098/17651/chieu-minh-vuong-tran-quang-khai.html
    ('tran-quoc-toan', $bio$Hoài Văn hầu Trần Quốc Toản là một người trẻ thuộc tôn thất nhà Trần trong giai đoạn chuẩn bị và tiến hành cuộc kháng chiến chống quân Nguyên – Mông lần thứ hai. Đại Việt sử ký toàn thư ghi chuyện năm 1282, khi triều đình tổ chức hội nghị bàn kế chống giặc. Vì còn trẻ nên Trần Quốc Toản không được tham dự, từ đó gắn với câu chuyện bóp nát quả cam vì phẫn chí.

Sau sự kiện này, sử liệu ghi rằng Trần Quốc Toản tự tập hợp hơn một nghìn gia nô và thân thuộc, chuẩn bị vũ khí, đóng chiến thuyền và dựng lá cờ mang sáu chữ “Phá cường địch, báo hoàng ân”, thể hiện quyết tâm chống quân xâm lược và báo đáp triều đình. Năm 1285, ông cùng các tướng nhà Trần tham gia trận Chương Dương.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3096/6830/tran-binh-trong-danh-tuong-khi-tiet.html
    ('tran-binh-trong', $bio$Trần Bình Trọng là một danh tướng thời Trần, nổi bật trong cuộc kháng chiến chống quân Nguyên – Mông năm 1285. Theo Bảo tàng Lịch sử Quốc gia, ông có nguồn gốc từ dòng họ Lê và được cho là con của Lê Tần, một danh tướng có công dưới triều Trần. Về sau ông mang họ Trần.

Trong giai đoạn quân Nguyên tiến sâu vào Đại Việt, Trần Bình Trọng được giao nhiệm vụ chặn quân địch tại khu vực Đà Mạc – Thiên Mạc, tạo thời gian để vua và bộ chỉ huy quân Trần rút lui an toàn. Do lực lượng chênh lệch, ông bị quân Nguyên bắt sau khi chiến đấu chống lại lực lượng truy kích. Ông khước từ lời dụ hàng và bị giết.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/16587/danh-tuong-tran-hai-chien-van-djon.html
    ('tran-khanh-du', $bio$Nhân Huệ vương Trần Khánh Dư là tướng nhà Trần từng trấn giữ vùng biển Vân Đồn. Trong cuộc kháng chiến chống quân Nguyên lần thứ ba, ông cho quân đón đánh đoàn thuyền chở lương do Trương Văn Hổ chỉ huy.

Chiến thắng Vân Đồn khiến quân Nguyên mất nguồn tiếp tế quan trọng và góp phần tạo điều kiện cho thắng lợi Bạch Đằng năm 1288.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/3098/16591/danh-tuong-pham-ngu-lao-1255-1320.html
    ('pham-ngu-lao', $bio$Phạm Ngũ Lão quê ở làng Phù Ủng, vùng Hưng Yên. Ông xuất thân từ tầng lớp bình dân và về sau trở thành gia tướng của Hưng Đạo Vương Trần Quốc Tuấn. Dưới sự dìu dắt của Trần Quốc Tuấn, Phạm Ngũ Lão trở thành một trong những tướng lĩnh tiêu biểu của triều Trần.

Ông có công trong hai cuộc kháng chiến chống quân Nguyên – Mông vào các năm 1285 và 1288. Trong cuộc kháng chiến năm 1285, Phạm Ngũ Lão tham gia các hoạt động quân sự tại Chương Dương và truy kích quân Nguyên rút chạy. Đến cuộc kháng chiến năm 1288, ông tiếp tục tham gia đánh chặn và truy kích quân địch. Sau chiến tranh, ông còn nhiều lần tham gia các chiến dịch quân sự khác và được triều đình giao nhiều chức vụ quan trọng.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/19035/hai-danh-tuong-djuoc-khac-trong-di-san-tu-lieu-the-gioi-ve-su-trung-thanh.html
    ('da-tuong', $bio$Dã Tượng là một trong những gia tướng thân cận của Hưng Đạo Vương Trần Quốc Tuấn, thường được nhắc đến cùng Yết Kiêu. Sử liệu về thân thế và cuộc đời của Dã Tượng khá ít, vì vậy những thông tin như năm sinh, năm mất hay diện mạo của ông hiện không có cơ sở chắc chắn để xác định.

Trong cuộc kháng chiến năm 1285, khi Trần Hưng Đạo định rút theo đường núi, Dã Tượng khuyên ông trở lại Bãi Tân vì tin Yết Kiêu vẫn giữ thuyền chờ chủ tướng. Đại Việt sử ký toàn thư chép rằng quả nhiên Yết Kiêu vẫn ở đó, giúp Trần Hưng Đạo rút lui bằng đường thủy.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/19035/hai-danh-tuong-djuoc-khac-trong-di-san-tu-lieu-the-gioi-ve-su-trung-thanh.html
    -- https://baotanglichsu.vn/vi/Articles/2001/66862/djen-son-hai.html
    ('yet-kieu', $bio$Yết Kiêu là một trong những gia tướng thân cận của Trần Hưng Đạo, cùng với Dã Tượng được sử liệu nhắc đến về lòng trung thành và tài năng quân sự. Ông đặc biệt nổi tiếng với khả năng bơi lặn và hoạt động dưới nước, qua đó trở thành một hình tượng tiêu biểu của lực lượng thủy quân thời Trần. Bảo tàng Lịch sử Quốc gia ghi nhận Yết Kiêu có biệt tài thủy chiến và có công trong cuộc kháng chiến chống quân Nguyên – Mông thế kỷ XIII.

Trong cuộc kháng chiến năm 1285, ông giữ thuyền ở Bãi Tân để đợi Trần Hưng Đạo dù quân phòng thủ đã rút. Tư liệu thờ tự ở làng Hạ Bì lưu truyền tên ông là Phạm Hữu Thế.$bio$),

    -- https://scov.gov.vn/dat-nuoc-con-nguoi/con-nguoi-viet-nam/luong-quoc-trang-nguyen-mac-dinh-chi.html
    -- https://sovhtt.hanoi.gov.vn/ky-niem-680-nam-ngay-mat-trang-nguyen-mac-dinh-chi-ton-vinh-truyen-thong-hieu-hoc-viet-nam/
    ('mac-dinh-chi', $bio$Mạc Đĩnh Chi là một danh thần và nhà ngoại giao nổi tiếng dưới triều Trần. Ông quê ở làng Lũng Động, vùng Nam Sách xưa. Năm 1304, dưới triều vua Trần Anh Tông, ông đỗ đầu kỳ thi và được xác định là Trạng nguyên.

Ông nổi tiếng về học vấn và khả năng ứng đối. Năm 1308, Mạc Đĩnh Chi được cử đi sứ nhà Nguyên. Các câu chuyện về tài ngoại giao và ứng đối của ông về sau trở thành một phần nổi bật trong hình tượng Mạc Đĩnh Chi. Danh xưng “Lưỡng quốc Trạng nguyên” cũng gắn liền với hình tượng này. Ông làm quan qua ba đời vua Trần Anh Tông, Trần Minh Tông và Trần Hiến Tông.$bio$),

    -- https://vnmh.com.vn/vi/Articles/3098/14470/tai-tri-cua-vua-le-thai-to-trong-muoi-nam-khang-chien-chong-quan-minh-1418-1427.html
    -- https://scov.gov.vn/dat-nuoc-con-nguoi/tom-tat-bien-nien-su-vn/xviii.-trieu-le-so-99-nam-1428-1527-quoc-hieu-dai-viet-kinh-do-dong-do-ha-noi-.html
    ('le-loi', $bio$Lê Lợi sinh năm 1385 tại vùng Lam Sơn, Thanh Hóa, xuất thân từ một gia đình hào trưởng. Sau khi nhà Minh thiết lập ách cai trị ở Đại Việt, năm 1418, ông cùng các hào kiệt dựng cờ khởi nghĩa tại Lam Sơn, mở đầu cuộc kháng chiến kéo dài khoảng mười năm chống quân Minh.

Trong những năm đầu, nghĩa quân Lam Sơn gặp nhiều khó khăn và nhiều lần phải rút về vùng rừng núi. Từ năm 1424, lực lượng nghĩa quân phát triển mạnh hơn, mở rộng hoạt động từ Nghệ An ra Bắc. Đến năm 1427, các chiến thắng lớn, đặc biệt tại Chi Lăng – Xương Giang, góp phần đánh bại lực lượng viện binh nhà Minh và đưa cuộc chiến đến hồi kết.

Năm 1428, Lê Lợi lên ngôi Hoàng đế, lấy niên hiệu Thuận Thiên, khôi phục quốc hiệu Đại Việt và trở thành vị vua đầu tiên của triều Hậu Lê. Ông mất năm 1433 và được an táng tại Vĩnh Lăng, Lam Sơn.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/18122/nguyen-trai-anh-hung-dan-toc-danh-nhan-van-hoa-1380-1442.html
    ('nguyen-trai-1', $bio$Nguyễn Trãi sinh năm 1380. Cha ông là Nguyễn Phi Khanh, trước tên là Nguyễn Ứng Long. Mẹ ông là Trần Thị Thái, con gái của quan Tư đồ Trần Nguyên Đán. Năm 1400, Nguyễn Trãi đỗ Thái học sinh và từng làm quan dưới triều Hồ.

Sau khi Đại Việt rơi vào sự cai trị của nhà Minh, Nguyễn Trãi tham gia khởi nghĩa Lam Sơn do Lê Lợi lãnh đạo. Ông giữ vai trò quan trọng về mưu lược, chính trị và ngoại giao, đồng thời thay mặt Lê Lợi soạn nhiều văn thư gửi các tướng nhà Minh. Sau thắng lợi của khởi nghĩa Lam Sơn, Nguyễn Trãi soạn Bình Ngô đại cáo, một trong những tác phẩm tiêu biểu nhất trong sự nghiệp của ông.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/3098/14509/thai-uy-phuc-quoc-cong-le-lai-dje-nhat-khai-quoc-cong-than-trieu-le.html
    -- https://baotanglichsu.vn/VI/Articles/3091/14734/hoi-thao-khoa-hoc-hoi-the-lung-nhai-trong-khoi-nghia-lam-son.html
    ('le-lai', $bio$Lê Lai là một trong những tướng lĩnh tham gia phong trào Lam Sơn từ giai đoạn đầu. Ông quê ở vùng Dựng Tú, Thanh Hóa, thuộc một gia đình có thế lực tại địa phương. Ông cùng Lê Lợi và các tướng lĩnh tham gia Hội thề Lũng Nhai, đặt nền móng cho lực lượng khởi nghĩa Lam Sơn. Khi khởi nghĩa chính thức bùng nổ năm 1418, Lê Lai được phong Quan Nội Hầu, giữ chức Tổng quản phủ Đô Tổng quản.

Theo ghi chép về giai đoạn đầu của khởi nghĩa, khi nghĩa quân bị vây ở vùng núi Chí Linh, Lê Lai khoác áo của Lê Lợi và dẫn quân ra đánh để đánh lạc hướng quân Minh. Ông hy sinh, giúp Lê Lợi cùng lực lượng chủ chốt thoát vây.$bio$),

    -- https://scov.gov.vn/ban-sac-van-hoa/viet-su-giai-thoai/vu-an-le-sat.html
    -- https://chauson.langson.gov.vn/thong-tin-tuyen-truyen/dinh-huong-tuyen-truyen/ky-niem-599-nam-ngay-chien-thang-chi-lang-10-10-1427-10-10-2026-.html
    ('le-sat', $bio$Lê Sát quê ở làng Bỉ Ngũ, vùng Lam Sơn, là một trong những tướng tham gia cuộc khởi nghĩa Lam Sơn từ giai đoạn đầu. Ông được sử liệu mô tả là người có tài trí và dũng lược, được Lê Lợi tin cậy giao quyền chỉ huy quân đội. Trong quá trình chống quân Minh, Lê Sát lập nhiều chiến công và trở thành một trong những võ tướng quan trọng của nghĩa quân.

Đến năm 1427, khi cuộc khởi nghĩa bước vào giai đoạn quyết định, Lê Sát cùng các tướng Lam Sơn chặn đánh đạo viện binh do Liễu Thăng chỉ huy tại Chi Lăng. Sau khi Lê Lợi lên ngôi năm 1428, ông tiếp tục được trao những chức vụ cao trong triều đình. Năm 1429, khi triều đình khắc tên 93 khai quốc công thần, Lê Sát được xếp ở hàng thứ hai trong danh sách được ghi nhận.$bio$),

    -- https://tapchiqptd.vn/vi/lich-su-quan-su-viet-nam/nghe-thuat-nghi-binh-tao-the-trong-tran-tot-dong-chuc-dong-nam-1426/26264.html
    -- https://scov.gov.vn/ban-sac-van-hoa/viet-su-giai-thoai/dinh-le-va-nguyen-xi-voi-cai-gia-cua-mot-bai-hoc.html
    ('dinh-le', $bio$Đinh Lễ là một trong những võ tướng nổi bật của nghĩa quân Lam Sơn. Ông là anh ruột của Đinh Liệt. Các tư liệu về Đinh Liệt cũng ghi nhận hai anh em sớm tham gia lực lượng của Lê Lợi trong cuộc khởi nghĩa chống quân Minh.

Trong giai đoạn nghĩa quân tiến vào Nghệ An, Đinh Lễ có vai trò đáng chú ý tại trận Khả Lưu năm 1424–1425. Ông được giao bố trí quân mai phục tại khu vực hiểm yếu, phối hợp với lực lượng của Đinh Liệt đánh quân Minh, góp phần vào quá trình mở rộng địa bàn hoạt động của nghĩa quân Lam Sơn ở Nghệ An.

Năm 1426, ông cùng Nguyễn Xí đem quân tiếp ứng ở Tốt Động – Chúc Động, góp phần đánh bại đạo quân do Vương Thông chỉ huy. Năm 1427, ông bị quân Minh bắt và giết sau trận đánh ở vùng My Động.$bio$),

    -- https://hannom.vass.gov.vn/tong-muc-luc/10-tim-lai-dau-xua-mot-danh-nhan-lich-su-tbhnh-1998-505635
    -- https://tapchiqptd.vn/vi/lich-su-quan-su-viet-nam/nghe-thuat-nghi-binh-tao-the-trong-tran-tot-dong-chuc-dong-nam-1426/26264.html
    ('pham-van-xao', $bio$Phạm Văn Xảo là tướng của nghĩa quân Lam Sơn dưới quyền Lê Lợi. Năm 1426, ông cùng Lý Triện chỉ huy một cánh quân tiến ra Bắc. Ông góp công trong chiến dịch Tốt Động – Chúc Động, nơi nghĩa quân đánh bại cuộc phản công do tướng Minh Vương Thông chỉ huy.

Sau khi nhà Lê thành lập năm 1428, ông được ban họ Lê và thăng hàm Thái bảo. Ông được ghi tên trong hàng ngũ khai quốc công thần của triều Lê sơ.$bio$),

    -- https://dulichphutho.gov.vn/den-tho-ta-tuong-quoc-tran-nguyen-han-dau-an-vi-danh-tuong-lam-son-tren-dat-son-dong.html
    -- https://vov.gov.vn/ta-tuong-quoc-tran-nguyen-han-nha-quan-su-dai-tai-dtnew-302445
    ('tran-nguyen-han', $bio$Trần Nguyên Hãn là một trong những tướng lĩnh quan trọng của khởi nghĩa Lam Sơn. Ông thuộc dòng dõi nhà Trần, là hậu duệ của Trần Quang Khải và cháu nội của Đại Tư đồ Trần Nguyên Đán. Năm sinh của ông chưa được xác định chắc chắn.

Sau khi theo Lê Lợi, Trần Nguyên Hãn được giao nhiều nhiệm vụ quân sự quan trọng. Năm 1425, ông tham gia chỉ huy lực lượng tiến vào Tân Bình và Thuận Hóa. Đến giai đoạn cuối của cuộc chiến, ông tham gia vây Đông Quan và chiến dịch tiêu diệt viện binh nhà Minh tại Xương Giang năm 1427. Sau thắng lợi của khởi nghĩa Lam Sơn, năm 1428 ông được phong Tả Tướng quốc, trở thành một trong những công thần có địa vị cao của triều Lê sơ.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/3098/19477/danh-tuong-le-khoi-va-djen-tho-569-nam-tuoi.html
    ('le-khoi', $bio$Lê Khôi là người vùng Lam Sơn, Thanh Hóa, con của Lê Trừ và là cháu ruột của Lê Lợi. Cha mẹ mất sớm nên ông sống cùng Lê Lợi và tham gia lực lượng Lam Sơn từ giai đoạn đầu. Bảo tàng Lịch sử Quốc gia ghi nhận ông có tên trong những người tham gia Hội thề Lũng Nhai và lập nhiều chiến công trong cuộc khởi nghĩa chống quân Minh.

Trong giai đoạn cuối của khởi nghĩa, Lê Khôi giữ vai trò đáng kể trên chiến trường. Năm 1427, tại chiến dịch Xương Giang, ông cùng Phạm Vấn đem quân chi viện cho lực lượng của Lê Sát và Trần Nguyên Hãn, góp phần đánh bại quân Minh và bắt nhiều tướng địch. Sau khi nhà Lê được thành lập, ông tiếp tục làm quan qua ba triều Lê Thái Tổ, Lê Thái Tông và Lê Nhân Tông.$bio$),

    -- https://dsvh.gov.vn/lang-mo-va-den-tho-nguyen-xi-tinh-nghe-an-3309
    -- https://scov.gov.vn/dat-nuoc-con-nguoi/con-nguoi-viet-nam/huu-tuong-quoc-nguyen-xi.html
    ('nguyen-xi', $bio$Nguyễn Xí quê ở vùng Thượng Xá, Chân Phúc, Nghệ An. Từ khi còn trẻ, ông cùng anh là Nguyễn Biện đến Lam Sơn và trở thành người thân cận của Lê Lợi. Khi khởi nghĩa Lam Sơn bùng nổ năm 1418, Nguyễn Xí được giao cùng Đinh Lễ chỉ huy đội quân Thiết đột, lực lượng quan trọng bảo vệ bộ chỉ huy nghĩa quân.

Trong gần mười năm khởi nghĩa, Nguyễn Xí tham gia nhiều chiến dịch lớn, từ những năm đầu gian khó ở vùng Thanh Hóa – Nghệ An đến Tốt Động – Chúc Động và bao vây Đông Quan. Sau khi cuộc khởi nghĩa thắng lợi, ông trở thành một trong những công thần quan trọng của triều Lê sơ.

Điểm đặc biệt của Nguyễn Xí là sự nghiệp của ông tiếp tục kéo dài nhiều thập niên sau chiến tranh. Ông phục vụ qua các triều Lê Thái Tổ, Lê Thái Tông, Lê Nhân Tông và Lê Thánh Tông. Năm 1460, Nguyễn Xí cùng một số đại thần tham gia phế truất Lê Nghi Dân và đưa Lê Tư Thành lên ngôi, tức Lê Thánh Tông. Sau đó ông được trao nhiều chức tước cao trong triều đình.$bio$),

    -- https://dsvh.gov.vn/di-tich-lich-su-de%CC%80n-tho%CC%80-nguye%CC%83n-bi%CC%89nh-khiem-2993
    ('nguyen-binh-khiem', $bio$Nguyễn Bỉnh Khiêm quê tại làng Trung Am, huyện Vĩnh Lại, trấn Hải Dương, nay thuộc khu vực Vĩnh Bảo, Hải Phòng. Năm 1535, dưới triều Mạc Thái Tông, ông dự thi và đỗ Trạng nguyên. Sau đó, ông làm quan cho triều Mạc và đảm nhiệm nhiều chức vụ quan trọng.

Sau khoảng tám năm làm quan, Nguyễn Bỉnh Khiêm rời quan trường và trở về quê, dựng am Bạch Vân và mở trường dạy học. Ông có ảnh hưởng lớn trong đời sống văn hóa và tư tưởng Việt Nam thế kỷ XVI, được hậu thế thường gọi là Trạng Trình. Ông mất năm 1585.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3096/6119/vua-quang-trung-nhung-djinh-menh-lich-su.html
    -- https://viensuhoc.vass.gov.vn/bao-ve-nen-tang-tu-tuong-cua-dang/chien-thang-ngoc-hoi-dong-da-mua-xuan-nam-ky-dau-1789-dau-an-lich-su-va-van-hoa-560605
    ('quang-trung', $bio$Nguyễn Huệ là một trong ba anh em lãnh đạo phong trào Tây Sơn, cùng với Nguyễn Nhạc và Nguyễn Lữ. Từ phong trào khởi nghĩa bắt đầu năm 1771, lực lượng Tây Sơn từng bước đánh bại các thế lực Nguyễn ở phía Nam và Trịnh ở phía Bắc. Bảo tàng Lịch sử Quốc gia ghi nhận Nguyễn Huệ là vị hoàng đế thứ hai của triều Tây Sơn và trị vì với niên hiệu Quang Trung từ 1788 đến 1792.

Một trong những chiến công quân sự nổi bật của Nguyễn Huệ là trận Rạch Gầm – Xoài Mút năm 1785, nơi quân Tây Sơn đánh bại lực lượng Xiêm tại khu vực sông Tiền. Sau đó, khi quân Thanh tiến vào Đại Việt cuối năm 1788, Nguyễn Huệ lên ngôi hoàng đế tại Phú Xuân rồi nhanh chóng đưa quân ra Bắc. Đầu xuân Kỷ Dậu 1789, quân Tây Sơn tiến công Hà Hồi, Ngọc Hồi và Đống Đa, sau đó tiến vào Thăng Long.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3096/12481/nguyen-nhac-voi-vuong-trieu-tay-son-mot-trieu-djai-toa-sang.html
    -- https://scov.gov.vn/dat-nuoc-con-nguoi/tom-tat-bien-nien-su-vn/xxi.-nha-tay-son-24-nam-1778-1802-kinh-do-phu-xuan-hue-.html
    ('nguyen-nhac-si', $bio$Nguyễn Nhạc là anh cả trong ba anh em Tây Sơn và được xem là người khởi xướng phong trào. Trước cuộc khởi nghĩa, ông từng làm nghề buôn bán trầu, nhờ đó có điều kiện đi lại giữa vùng đồng bằng và Tây Sơn Thượng đạo, xây dựng quan hệ và tập hợp lực lượng. Khoảng năm 1771, phong trào Tây Sơn bắt đầu phát triển dưới sự lãnh đạo của Nguyễn Nhạc cùng Nguyễn Huệ và Nguyễn Lữ.

Năm 1773, lực lượng Tây Sơn từ căn cứ vùng thượng đạo tiến xuống đồng bằng và chiếm được phần lớn khu vực Quy Nhơn. Đến năm 1778, Nguyễn Nhạc lên ngôi Hoàng đế, lấy niên hiệu Thái Đức, đặt trung tâm chính quyền tại Thành Hoàng Đế. Di tích này từng là đại bản doanh của quân Tây Sơn và sau đó trở thành kinh đô của chính quyền Nguyễn Nhạc.$bio$),

    -- https://scov.gov.vn/dat-nuoc-con-nguoi/tom-tat-bien-nien-su-vn/xxi.-nha-tay-son-24-nam-1778-1802-kinh-do-phu-xuan-hue-.html
    -- https://baotanglichsu.vn/VI/Articles/3096/12503/nguyen-anh-voi-vung-djat-gia-djinh.html
    ('nguyen-lu-si', $bio$Nguyễn Lữ là một trong ba anh em Tây Sơn cùng Nguyễn Nhạc và Nguyễn Huệ lãnh đạo phong trào khởi nghĩa bùng nổ năm 1771. Bảo tàng Lịch sử Quốc gia ghi nhận ông là một thành viên quan trọng của phong trào từ giai đoạn đầu và được giao nhiều trọng trách quân sự.

Năm 1776, Nguyễn Lữ được phong Thiếu phó và tham gia điều hành vùng đất do Tây Sơn kiểm soát. Năm 1777, ông cùng Nguyễn Huệ tiếp tục tiến quân vào khu vực Nam Bộ chống lực lượng của chúa Nguyễn. Khi anh em Tây Sơn phân chia địa bàn cai quản năm 1787, Nguyễn Lữ được phong Đông Định vương, phụ trách vùng Gia Định.$bio$),

    -- https://svhttdl.gialai.gov.vn/van-hoa-nghe-thuat/ky-niem-219-nam-ngay-mat-do-doc-bui-thi-xuan-1802-2021-tu-hao-nho-ve-nu-tuong-148.html
    -- https://baotanglichsu.vn/VI/Articles/3098/14999/su-thuc-cai-chet-cua-nu-tuong-bui-thi-xuan-va-chong-2.html
    ('bui-thi-xuan', $bio$Đô đốc Bùi Thị Xuân là nữ tướng của nhà Tây Sơn, quê ở vùng Tây Sơn, Bình Định. Sau khi Quang Trung mất, bà tiếp tục phục vụ triều Cảnh Thịnh và chỉ huy lực lượng bảo vệ kinh đô Phú Xuân.

Năm 1802, bà chỉ huy quân Tây Sơn chiến đấu tại lũy Trấn Ninh trong cuộc chiến với quân Nguyễn Ánh. Bà qua đời cùng năm. Đền thờ Đô đốc Bùi Thị Xuân ở quê hương là nơi tưởng niệm bà.$bio$),

    -- https://baotanglichsu.vn/VI/Articles/3098/14973/thai-pho-tran-quang-dieu-danh-tuong-nhan-djuc-cua-trieu-tay-son.html
    ('tran-quang-dieu', $bio$Trần Quang Diệu là một trong những tướng lĩnh quan trọng của nhà Tây Sơn. Bảo tàng Lịch sử Quốc gia ghi ông mất năm 1802, là chồng của nữ tướng Bùi Thị Xuân và thuộc nhóm các tướng nổi bật thường được hậu thế gọi là Tây Sơn Thất hổ tướng.

Ông tham gia phong trào Tây Sơn từ giai đoạn sớm và trở thành một trong những tướng lĩnh trụ cột. Sau chiến thắng Kỷ Dậu 1789, Trần Quang Diệu được giao làm Đốc trấn Nghệ An và phụ trách việc xây dựng Phượng Hoàng Trung Đô, công trình gắn với kế hoạch của Quang Trung tại Nghệ An.

Sau khi Quang Trung mất năm 1792, Trần Quang Diệu tiếp tục phục vụ triều Cảnh Thịnh. Trong giai đoạn cuối của Tây Sơn, ông chỉ huy nhiều chiến dịch chống lực lượng Nguyễn Ánh. Trong chiến dịch vây thành Quy Nhơn, ông tiếp nhận quân đối phương sau khi Võ Tánh mất và cho an táng vị tướng giữ thành.$bio$),

    -- https://svhttdl.gialai.gov.vn/hoat-dong-nganh/le-khanh-thanh-den-tho-dai-tu-do-vo-van-dung-244.html
    -- https://baotanglichsu.vn/vi/Articles/3098/13755/tay-son-that-ho-tuong-ky-2-trung-than-khong-tho-hai-vua.html
    ('vo-van-dung', $bio$Võ Văn Dũng quê ở vùng Tuy Viễn, phủ Quy Nhơn, thuộc khu vực Tây Sơn – Bình Định trước đây. Tư liệu giới thiệu tại Đền thờ Đại tư đồ Võ Văn Dũng cho biết ông tham gia phong trào Tây Sơn từ giai đoạn đầu và dần trở thành một võ tướng quan trọng của lực lượng này.

Trong quá trình hoạt động, Võ Văn Dũng tham gia nhiều chiến dịch của quân Tây Sơn. Nguồn giới thiệu di tích ghi nhận ông tham gia các hoạt động quân sự tại Gia Định, chiến dịch Rạch Gầm – Xoài Mút, cuộc tiến quân ra Bắc năm 1786 và các hoạt động quân sự về sau của triều Tây Sơn.

Võ Văn Dũng cũng đảm nhiệm công việc ngoại giao. Sau chiến thắng năm 1789, ông được Quang Trung giao nhiệm vụ đi sứ nhằm thiết lập quan hệ với nhà Thanh. Năm 1791, ông giữ chức Chiêu Viễn Đại Đô đốc, tước Võ Quốc công. Đến thời Cảnh Thịnh, ông được phong Đại Tư đồ.$bio$),

    -- https://giaoducthoidai.vn/tay-son-that-ho-tuong-le-van-hung-tu-tuong-cuop-thanh-danh-tuong-post580235.html
    -- https://baotanglichsu.vn/vi/Articles/3098/13757/tay-son-that-ho-tuong-ky-4-vi-dan-vi-nuoc-gac-chuyen-phuc-thu.html
    ('le-van-hung', $bio$Lê Văn Hưng được ghi nhận là người vùng Kiên Dõng, Bình Khê, thuộc khu vực Tây Sơn – Bình Định ngày nay. Ông xuất thân bình dân và gia nhập nghĩa quân Tây Sơn từ cấp binh lính, sau đó dần trở thành một võ tướng. Nhiều câu chuyện về thời trẻ của ông được lưu truyền trong võ sử Bình Định.

Trong quân đội Tây Sơn, Lê Văn Hưng được nhắc đến như một tướng có võ nghệ cao. Truyền thống võ học Bình Định đặc biệt gắn ông với côn, còn gọi là roi trường. Các tài liệu về “Tây Sơn Thất hổ tướng” xếp ông cùng Võ Văn Dũng, Trần Quang Diệu, Võ Đình Tú, Nguyễn Văn Tuyết, Lý Văn Bưu và Nguyễn Văn Lộc.

Trong quá trình chiến đấu giữa Tây Sơn và lực lượng Nguyễn, Lê Văn Hưng từng hoạt động tại Diên Khánh và Bình Thuận. Một số nguồn ghi nhận ông lần lượt giữ các chức Đề đốc, Đô đốc, Đại Đô đốc, Thái úy và về sau giữ chức Binh bộ Thượng thư dưới triều Cảnh Thịnh.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/3098/13757/tay-son-that-ho-tuong-ky-4-vi-dan-vi-nuoc-gac-chuyen-phuc-thu.html
    ('nguyen-van-loc', $bio$Nguyễn Văn Lộc được Địa chí Bình Định ghi là người làng Kỳ Sơn, huyện Tuy Viễn. Ông thuộc nhóm bảy võ tướng Bình Định được hậu thế gọi là Tây Sơn Thất hổ tướng, cùng Võ Văn Dũng, Trần Quang Diệu, Võ Đình Tú, Nguyễn Văn Tuyết, Lý Văn Bưu và Lê Văn Hưng.

Trong giai đoạn đầu của phong trào Tây Sơn, Nguyễn Văn Lộc được phong Hữu Đô đốc. Cuối năm 1773, ông cùng Lê Văn Hưng tham gia lực lượng tiến xuống phía Nam, hoạt động tại Phú Yên, Diên Khánh và Bình Thuận.

Năm 1786, Nguyễn Văn Lộc giữ vai trò Thủy quân Đô đốc, tham gia chiến dịch của Nguyễn Huệ đánh Thuận Hóa.

Trong chiến dịch chống quân Thanh 1788–1789, Nguyễn Văn Lộc được phong Đại Đô đốc, chỉ huy một cánh quân tiến theo hướng Lạng Sơn – Phượng Nhãn và bố trí lực lượng nhằm chặn đường rút của quân Thanh. Đây là một trong những hoạt động quân sự nổi bật nhất gắn với tên tuổi ông.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/3098/13757/tay-son-that-ho-tuong-ky-4-vi-dan-vi-nuoc-gac-chuyen-phuc-thu.html
    -- https://baotanglichsu.vn/DataFiles/2024/03/News/Tieng%20Viet/4.3.2024/Thong%20bao%20Khoa%20hoc%20so%202.2023/005.pdf
    ('nguyen-van-tuyet', $bio$Nguyễn Văn Tuyết là một tướng lĩnh của nhà Tây Sơn, thường được xếp cùng Võ Văn Dũng, Võ Đình Tú, Trần Quang Diệu, Lê Văn Hưng, Lý Văn Bưu và Nguyễn Văn Lộc trong nhóm được hậu thế gọi là Tây Sơn Thất hổ tướng. Tài liệu địa phương và các công trình viết về Tây Sơn thường ghi ông quê ở An Nhơn, huyện Tuy Viễn, phủ Quy Nhơn. Các tư liệu gia phả còn ghi những thông tin khác nhau về thân thế của ông.

Nguyễn Văn Tuyết nổi bật trong chiến dịch chống quân Thanh cuối năm 1788 – đầu năm 1789. Khi quân Thanh tiến vào Thăng Long, ông được giao nhiệm vụ nhanh chóng về Phú Xuân báo tin cho Nguyễn Huệ. Sau khi Quang Trung tiến quân ra Bắc, Nguyễn Văn Tuyết được giao chỉ huy một cánh quân hoạt động theo hướng Hải Dương, phối hợp với các đạo quân khác trong chiến dịch.$bio$),

    -- https://tayson.gialai.gov.vn/tin-tuc/tin-van-hoa-xa-hoi/khoi-dong-du-an-phim-da-su-hanh-dong-tay-son-that-ho-tuong-ky-nam-cung-thu-ly-van-buu2.html
    -- https://baotanglichsu.vn/VI/Articles/3098/13754/tay-son-that-ho-tuong-ky-1-vi-tuong-diet-ac-tru-gian.html
    ('ly-van-bu', $bio$Lý Văn Bưu là một trong bảy võ tướng thường được hậu thế gọi là Tây Sơn Thất hổ tướng, cùng Võ Văn Dũng, Trần Quang Diệu, Võ Đình Tú, Nguyễn Văn Tuyết, Lê Văn Hưng và Nguyễn Văn Lộc.

Các tài liệu địa phương ghi ông quê làng Đại Khoang, huyện Phù Cát, Bình Định, xuất thân trong gia đình có nghề nuôi và buôn bán ngựa. Nhờ môi trường này, hình tượng Lý Văn Bưu về sau đặc biệt gắn với cưỡi ngựa, huấn luyện chiến mã và bắn cung. Năm sinh và năm mất của ông hiện chưa được xác định rõ.

Khi tham gia phong trào Tây Sơn, Lý Văn Bưu được phong Đô đốc và được ghi nhận có vai trò trong việc tổ chức, huấn luyện lực lượng chiến mã. Tư liệu địa phương cũng ghi ông tham gia chiến dịch chống quân Xiêm ở phía Nam và sau đó theo Quang Trung tiến ra Bắc chống quân Thanh.$bio$),

    -- https://baotanglichsu.vn/vi/Articles/3098/13756/tay-son-that-ho-tuong-ky-3-thiet-con-vo-djich.html
    ('vo-dinh-tu', $bio$Võ Đình Tú là người thôn Phú Phong, huyện Tuy Viễn, phủ Quy Nhơn, thuộc vùng Tây Sơn, Bình Định. Ông là một trong bảy võ tướng được hậu thế gọi là Tây Sơn Thất hổ tướng. Bảo tàng Lịch sử Quốc gia cho biết tư liệu về ông không nhiều, nhưng Địa chí Bình Định và các tài liệu địa phương đều nhấn mạnh tài võ nghệ và mối quan hệ của ông với lực lượng Tây Sơn.

Theo các tài liệu này, Võ Đình Tú được Võ Văn Dũng giới thiệu với Nguyễn Nhạc khi phong trào Tây Sơn phát triển. Ông được Nguyễn Huệ tin cậy và tham gia bàn bạc việc quân. Trong giai đoạn đầu, Võ Đình Tú cùng Bùi Thị Xuân được giao quản lý và phòng thủ khu vực Tây Sơn.

Đặc điểm nổi tiếng nhất của Võ Đình Tú là võ côn. Các tài liệu về Tây Sơn kể rằng ông thông thạo côn, kiếm, thương, quyền, cưỡi ngựa và bắn cung, nhưng đặc biệt nổi danh với thiết côn. Danh xưng “Thiết côn vô địch” cũng gắn chặt với hình tượng của ông trong truyền thống võ học Tây Sơn.$bio$)
)
UPDATE hero_chess.historical_character AS c
SET description = b.biography
FROM biographies AS b
WHERE c.code = b.code
  AND COALESCE(c.description, '') ~ '^[[:space:]]*$';

-- Soldier archetypes have no historical character record.
WITH biographies(code, biography) AS (
    VALUES
    -- https://scov.gov.vn/dat-nuoc-con-nguoi/tom-tat-bien-nien-su-vn/xiv.-nha-tran-175-nam-1225-1400-quoc-hieu-dai-viet-kinh-do-thang-long-phan-1-.html
    -- https://tulieuvankien.dangcongsan.vn/van-kien-tu-lieu-ve-dang/book/sach-chinh-tri/lich-su-tu-tuong-quan-su-viet-nam-tap-2-tu-nam-1428-den-nam-1858-326
    -- https://baotanglichsu.vn/vi/Articles/3097/15641/ky-niem-726-nam-1288-2014-chien-thang-quan-nguyen-mong-lan-thu-3.html
    ('linh-nha-tran-soldier', $bio$Lính nhà Trần đại diện cho quân sĩ Đại Việt thế kỷ XIII. Triều đình duy trì quân thường trực và thực hiện chính sách ngụ binh ư nông, theo đó binh lính thay phiên tại ngũ và trở về sản xuất.

Quân dân nhà Trần đã tham gia ba cuộc kháng chiến chống quân Mông Cổ và nhà Nguyên vào các năm 1258, 1285 và 1287–1288. Trong cuộc kháng chiến thứ ba, thắng lợi ở Bạch Đằng năm 1288 góp phần kết thúc cuộc xâm lược.$bio$),

    -- https://scov.gov.vn/dat-nuoc-con-nguoi/tom-tat-bien-nien-su-vn/xviii.-trieu-le-so-99-nam-1428-1527-quoc-hieu-dai-viet-kinh-do-dong-do-ha-noi-.html
    -- https://tulieuvankien.dangcongsan.vn/van-kien-tu-lieu-ve-dang/book/sach-chinh-tri/lich-su-tu-tuong-quan-su-viet-nam-tap-2-tu-nam-1428-den-nam-1858-326
    ('linh-nha-le-soldier', $bio$Lính nhà Lê đại diện cho quân sĩ Đại Việt buổi đầu thời Lê sơ. Lực lượng này kế thừa đội ngũ nghĩa quân Lam Sơn từng theo Lê Lợi kháng chiến chống quân Minh.

Sau khi lên ngôi năm 1428, Lê Lợi tổ chức lại quân đội. Năm 1429, ông cho các vệ và quân năm đạo tập trận thủy bộ rồi chia phiên. Một phiên ở lại quân ngũ, các phiên còn lại về làm ruộng theo chính sách ngụ binh ư nông.$bio$),

    -- https://scov.gov.vn/dat-nuoc-con-nguoi/tom-tat-bien-nien-su-vn/xxi.-nha-tay-son-24-nam-1778-1802-kinh-do-phu-xuan-hue-.html
    -- https://viensuhoc.vass.gov.vn/bao-ve-nen-tang-tu-tuong-cua-dang/chien-thang-ngoc-hoi-dong-da-mua-xuan-nam-ky-dau-1789-dau-an-lich-su-va-van-hoa-560605
    ('linh-nha-tay-son-soldier', $bio$Lính nhà Tây Sơn đại diện cho quân sĩ của phong trào khởi nghĩa bắt đầu năm 1771 ở vùng Tây Sơn, Bình Định. Lực lượng này chiến đấu ở cả Đàng Trong và Đàng Ngoài vào những thập niên cuối thế kỷ XVIII.

Dưới quyền Nguyễn Huệ, quân Tây Sơn đánh bại quân Xiêm ở Rạch Gầm – Xoài Mút năm 1785. Mùa xuân Kỷ Dậu 1789, quân Tây Sơn do Hoàng đế Quang Trung chỉ huy đánh bại quân Thanh trong chiến dịch Ngọc Hồi – Đống Đa và tiến vào Thăng Long.$bio$)
)
UPDATE hero_chess.hero AS h
SET description = b.biography
FROM biographies AS b
WHERE h.code = b.code
  AND h.class_code = 'SOLDIER'
  AND h.character_id IS NULL
  AND COALESCE(h.description, '') ~ '^[[:space:]]*$';

COMMIT;
